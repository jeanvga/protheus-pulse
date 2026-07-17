using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Service.Endpoints;

public static class InstallationEndpoints
{
    private const int MaximumComponents = 50;
    private const int MaximumTags = 20;

    public static RouteGroupBuilder MapInstallationManagement(this RouteGroupBuilder api)
    {
        api.MapPost("/installations", CreateAsync).RequireAuthorization("Administrator");
        return api;
    }

    private static async Task<IResult> CreateAsync(
        CreateInstallationRequest request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var name = request.Name!.Trim();
        var existingNames = await dbContext.Installations
            .AsNoTracking()
            .Select(item => item.Name)
            .ToListAsync(cancellationToken);
        if (existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return Results.Conflict(new { message = "Já existe uma instalação com esse nome." });
        }

        var tags = (request.Tags ?? [])
            .Select(item => item!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var now = clock.UtcNow;
        var installation = new Installation
        {
            Name = name,
            Environment = request.Environment!.Value,
            CustomEnvironmentName = request.Environment == EnvironmentKind.Custom
                ? request.CustomEnvironmentName!.Trim()
                : null,
            TagsJson = JsonSerializer.Serialize(tags),
            IsDemo = false,
            CreatedAt = now
        };

        foreach (var item in request.Components!)
        {
            var validItem = item!;
            installation.Components.Add(new Component
            {
                InstallationId = installation.Id,
                Name = validItem.Name!.Trim(),
                Type = validItem.Type!.Value,
                Status = HealthStatus.Unknown,
                IsRequired = validItem.IsRequired,
                IsDemo = false
            });
        }

        var userId = GetUserId(principal);
        dbContext.Installations.Add(installation);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = userId,
            Action = "InstallationCreated",
            EntityType = nameof(Installation),
            EntityId = installation.Id.ToString(),
            SanitizedDetailsJson = JsonSerializer.Serialize(new
            {
                environment = installation.Environment,
                componentCount = installation.Components.Count,
                tagCount = tags.Length
            }),
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/installations/{installation.Id}", new InstallationCreatedResponse(
            installation.Id,
            installation.Name,
            installation.Environment,
            installation.CustomEnvironmentName,
            tags,
            installation.Components.Count,
            HealthStatus.Unknown));
    }

    private static Dictionary<string, string[]> Validate(CreateInstallationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        AddTextError(errors, "name", request.Name, 160, "Informe o nome da instalação.");

        if (request.Environment is null)
        {
            errors["environment"] = ["Informe o ambiente da instalação."];
        }
        else if (!Enum.IsDefined(request.Environment.Value))
        {
            errors["environment"] = ["Informe um ambiente válido."];
        }
        else if (request.Environment == EnvironmentKind.Custom)
        {
            AddTextError(errors, "customEnvironmentName", request.CustomEnvironmentName, 80, "Informe o nome do ambiente personalizado.");
        }

        if (request.Tags is not null && request.Tags.Count > MaximumTags)
        {
            errors["tags"] = [$"Informe no máximo {MaximumTags} tags."];
        }
        else if (request.Tags is not null && request.Tags.Any(item => !IsValidText(item, 40)))
        {
            errors["tags"] = ["Cada tag deve ter entre 1 e 40 caracteres e não pode conter caracteres de controle."];
        }

        if (request.Components is null || request.Components.Count == 0)
        {
            errors["components"] = ["Informe ao menos um componente."];
        }
        else if (request.Components.Count > MaximumComponents)
        {
            errors["components"] = [$"Informe no máximo {MaximumComponents} componentes."];
        }
        else
        {
            ValidateComponents(request.Components, errors);
        }

        return errors;
    }

    private static void ValidateComponents(
        IReadOnlyList<CreateComponentRequest?> components,
        IDictionary<string, string[]> errors)
    {
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            if (component is null)
            {
                errors[$"components[{index}]"] = ["Informe os dados do componente."];
                continue;
            }

            AddTextError(errors, $"components[{index}].name", component.Name, 160, "Informe o nome do componente.");
            if (component.Type is null || !Enum.IsDefined(component.Type.Value))
            {
                errors[$"components[{index}].type"] = ["Informe um tipo de componente válido."];
            }
        }

        var duplicateNames = components
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item!.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateNames.Length > 0)
        {
            errors["components"] = ["Os nomes dos componentes devem ser únicos dentro da instalação."];
        }
    }

    private static void AddTextError(
        IDictionary<string, string[]> errors,
        string key,
        string? value,
        int maximumLength,
        string requiredMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[key] = [requiredMessage];
        }
        else if (!IsValidText(value, maximumLength))
        {
            errors[key] = [$"O valor deve ter no máximo {maximumLength} caracteres e não pode conter caracteres de controle."];
        }
    }

    private static bool IsValidText(string? value, int maximumLength)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && trimmed.Length <= maximumLength
            && !trimmed.Any(char.IsControl);
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public sealed record CreateInstallationRequest(
        string? Name,
        EnvironmentKind? Environment,
        string? CustomEnvironmentName,
        IReadOnlyList<string?>? Tags,
        IReadOnlyList<CreateComponentRequest?>? Components);

    public sealed record CreateComponentRequest(string? Name, ComponentType? Type, bool IsRequired = true);

    public sealed record InstallationCreatedResponse(
        Guid Id,
        string Name,
        EnvironmentKind Environment,
        string? CustomEnvironmentName,
        IReadOnlyList<string> Tags,
        int ComponentCount,
        HealthStatus Status);
}
