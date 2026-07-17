using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Configuration;

namespace ProtheusPulse.Service.Endpoints;

public static class InstallationImportEndpoints
{
    public static RouteGroupBuilder MapInstallationImport(this RouteGroupBuilder api)
    {
        api.MapPost("/installations/import/preview", PreviewAsync).RequireAuthorization("Administrator");
        api.MapPost("/installations/import", ApplyAsync).RequireAuthorization("Administrator");
        return api;
    }

    private static async Task<IResult> PreviewAsync(
        ImportRequest request,
        PulseDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var parsed = InstallationDocumentParser.Parse(request.Format, request.Content);
        if (!parsed.IsValid)
        {
            return Results.Ok(new ImportPreview(false, 0, 0, 0, parsed.Errors, []));
        }

        var existingNames = await dbContext.Installations
            .AsNoTracking()
            .Select(item => item.Name)
            .ToListAsync(cancellationToken);
        var validation = InstallationImportValidator.Validate(parsed.Document!, existingNames);
        return Results.Ok(CreatePreview(parsed.Document!, validation));
    }

    private static async Task<IResult> ApplyAsync(
        ImportRequest request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!request.Confirm)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["confirm"] = ["Confirme explicitamente a importação após revisar a prévia."]
            });
        }

        var parsed = InstallationDocumentParser.Parse(request.Format, request.Content);
        if (!parsed.IsValid)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["content"] = parsed.Errors.ToArray() });
        }

        var existingNames = await dbContext.Installations
            .AsNoTracking()
            .Select(item => item.Name)
            .ToListAsync(cancellationToken);
        var validation = InstallationImportValidator.Validate(parsed.Document!, existingNames);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["content"] = validation.Errors.ToArray() });
        }

        var now = clock.UtcNow;
        var installations = parsed.Document!.Installations!
            .Select(definition => CreateInstallation(definition, now))
            .ToArray();
        dbContext.Installations.AddRange(installations);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = GetUserId(principal),
            Action = "InstallationsImported",
            EntityType = nameof(Installation),
            SanitizedDetailsJson = JsonSerializer.Serialize(new
            {
                installationCount = installations.Length,
                componentCount = installations.Sum(item => item.Components.Count),
                format = request.Format?.Trim().ToLowerInvariant()
            }),
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created("/api/v1/installations", new
        {
            imported = installations.Select(item => new { item.Id, item.Name }).ToArray(),
            validation.Warnings
        });
    }

    private static ImportPreview CreatePreview(
        InstallationImportDocument document,
        InstallationImportValidator.ValidationResult validation)
    {
        var installations = document.Installations ?? [];
        return new ImportPreview(
            validation.IsValid,
            document.SchemaVersion,
            installations.Count,
            installations.Sum(item => item.Components?.Count ?? 0),
            validation.Errors,
            validation.Warnings);
    }

    private static Installation CreateInstallation(InstallationDefinition definition, DateTimeOffset now)
    {
        var installation = new Installation
        {
            Name = definition.Name!.Trim(),
            Environment = Enum.Parse<EnvironmentKind>(definition.Environment!, ignoreCase: true),
            CustomEnvironmentName = string.IsNullOrWhiteSpace(definition.CustomEnvironmentName)
                ? null
                : definition.CustomEnvironmentName.Trim(),
            TagsJson = JsonSerializer.Serialize((definition.Tags ?? [])
                .Select(item => item!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)),
            CreatedAt = now,
            IsDemo = false
        };

        foreach (var definitionComponent in definition.Components!)
        {
            var componentDefinition = definitionComponent!;
            var component = new Component
            {
                Name = componentDefinition.Name!.Trim(),
                Type = Enum.Parse<ComponentType>(componentDefinition.Type!, ignoreCase: true),
                IsRequired = componentDefinition.IsRequired,
                Status = HealthStatus.Unknown,
                IsDemo = false
            };
            AddTargets(component, componentDefinition);
            installation.Components.Add(component);
        }

        return installation;
    }

    private static void AddTargets(Component component, ComponentDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.WindowsServiceName))
        {
            component.WindowsServiceTargets.Add(new WindowsServiceTarget
            {
                ServiceName = definition.WindowsServiceName.Trim()
            });
        }

        if (!string.IsNullOrWhiteSpace(definition.ExecutablePath))
        {
            component.ProcessTargets.Add(new ProcessTarget { ExecutablePath = definition.ExecutablePath.Trim() });
            component.FileTargets.Add(new FileTarget { Path = definition.ExecutablePath.Trim(), Kind = FileTargetKind.Executable });
        }

        if (!string.IsNullOrWhiteSpace(definition.IniPath))
        {
            component.FileTargets.Add(new FileTarget { Path = definition.IniPath.Trim(), Kind = FileTargetKind.Ini });
        }

        foreach (var path in definition.LogPaths ?? [])
        {
            component.LogSources.Add(new LogSource { Path = path!.Trim() });
        }

        foreach (var definitionCheck in definition.TcpChecks ?? [])
        {
            var check = definitionCheck!;
            component.TcpChecks.Add(new TcpCheck
            {
                Host = check.Host!.Trim(),
                Port = check.Port,
                TimeoutMs = check.TimeoutMs,
                IsRequired = check.IsRequired
            });
        }

        foreach (var definitionCheck in definition.HttpChecks ?? [])
        {
            var check = definitionCheck!;
            component.HttpChecks.Add(new HttpCheck
            {
                Url = check.Url!.Trim(),
                Method = check.Method.Trim().ToUpperInvariant(),
                ExpectedStatusMin = check.ExpectedStatusMin,
                ExpectedStatusMax = check.ExpectedStatusMax,
                TimeoutMs = check.TimeoutMs,
                BodyPattern = string.IsNullOrWhiteSpace(check.BodyPattern) ? null : check.BodyPattern.Trim(),
                ValidateTls = check.ValidateTls,
                CertificateWarningDays = check.CertificateWarningDays,
                IsRequired = check.IsRequired
            });
        }
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public sealed record ImportRequest(string? Format, string? Content, bool Confirm = false);
    public sealed record ImportPreview(
        bool Valid,
        int SchemaVersion,
        int InstallationCount,
        int ComponentCount,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings);
}
