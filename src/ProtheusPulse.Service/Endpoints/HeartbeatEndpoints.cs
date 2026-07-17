using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Service.Endpoints;

public static class HeartbeatEndpoints
{
    private const string TokenHeader = "X-Pulse-Heartbeat-Token";

    public static RouteGroupBuilder MapHeartbeats(this RouteGroupBuilder api)
    {
        api.MapGet("/heartbeat-definitions", GetDefinitionsAsync).RequireAuthorization("Viewer");
        api.MapPost("/heartbeat-definitions", CreateDefinitionAsync).RequireAuthorization("Administrator");
        api.MapPost("/heartbeat-definitions/{id:guid}/rotate", RotateTokenAsync).RequireAuthorization("Administrator");
        api.MapDelete("/heartbeat-definitions/{id:guid}", DeleteDefinitionAsync).RequireAuthorization("Administrator");
        api.MapPost("/heartbeats/{jobKey}", ReceiveAsync).AllowAnonymous().RequireRateLimiting("heartbeat");
        return api;
    }

    private static async Task<IResult> GetDefinitionsAsync(PulseDbContext dbContext, CancellationToken cancellationToken) =>
        Results.Ok(await dbContext.HeartbeatDefinitions.AsNoTracking()
            .OrderBy(item => item.Component.Installation.Name)
            .ThenBy(item => item.Component.Name)
            .ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                item.ComponentId,
                InstallationName = item.Component.Installation.Name,
                ComponentName = item.Component.Name,
                item.Name,
                item.JobKey,
                item.ExpectedIntervalSeconds,
                item.ToleranceSeconds,
                item.WindowStart,
                item.WindowEnd,
                item.LastHeartbeatAt
            })
            .ToListAsync(cancellationToken));

    private static async Task<IResult> CreateDefinitionAsync(
        CreateHeartbeatRequest request,
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

        if (!await dbContext.Components.AnyAsync(item => item.Id == request.ComponentId, cancellationToken))
        {
            return Results.NotFound(new { message = "Componente não encontrado." });
        }

        var jobKey = string.IsNullOrWhiteSpace(request.JobKey) ? CreateJobKey() : request.JobKey.Trim();
        if (await dbContext.HeartbeatDefinitions.AnyAsync(item => item.JobKey == jobKey, cancellationToken))
        {
            return Results.Conflict(new { message = "A chave pública do job já está em uso." });
        }

        var token = CreateToken();
        var definition = new HeartbeatDefinition
        {
            ComponentId = request.ComponentId!.Value,
            Name = request.Name!.Trim(),
            JobKey = jobKey,
            TokenHash = HashToken(token),
            ExpectedIntervalSeconds = request.ExpectedIntervalSeconds,
            ToleranceSeconds = request.ToleranceSeconds,
            WindowStart = request.WindowStart,
            WindowEnd = request.WindowEnd
        };
        dbContext.HeartbeatDefinitions.Add(definition);
        AddAudit(dbContext, clock, principal, httpContext, "HeartbeatDefinitionCreated", definition.Id, new
        {
            definition.ComponentId,
            definition.ExpectedIntervalSeconds,
            definition.ToleranceSeconds,
            hasWindow = definition.WindowStart.HasValue
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/heartbeat-definitions/{definition.Id}", TokenResponse(definition, token));
    }

    private static async Task<IResult> RotateTokenAsync(
        Guid id,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.HeartbeatDefinitions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        var token = CreateToken();
        definition.TokenHash = HashToken(token);
        AddAudit(dbContext, clock, principal, httpContext, "HeartbeatTokenRotated", definition.Id, new { definition.ComponentId });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(TokenResponse(definition, token));
    }

    private static async Task<IResult> DeleteDefinitionAsync(
        Guid id,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.HeartbeatDefinitions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        dbContext.HeartbeatDefinitions.Remove(definition);
        AddAudit(dbContext, clock, principal, httpContext, "HeartbeatDefinitionDeleted", definition.Id, new { definition.ComponentId });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ReceiveAsync(
        string jobKey,
        PulseDbContext dbContext,
        IClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var suppliedToken = httpContext.Request.Headers[TokenHeader].ToString();
        if (!IsValidJobKey(jobKey) || suppliedToken.Length is < 20 or > 256)
        {
            return Unauthorized();
        }

        var definition = await dbContext.HeartbeatDefinitions
            .Include(item => item.Component)
            .SingleOrDefaultAsync(item => item.JobKey == jobKey, cancellationToken);
        if (definition is null || !TokenMatches(suppliedToken, definition.TokenHash))
        {
            return Unauthorized();
        }

        var now = clock.UtcNow;
        definition.LastHeartbeatAt = now;
        dbContext.ProbeResults.Add(new ProbeResult
        {
            ComponentId = definition.ComponentId,
            ProbeType = ProbeType.Heartbeat,
            Status = HealthStatus.Healthy,
            ObservedAt = now,
            DurationMs = 0,
            Message = "Heartbeat autenticado recebido.",
            EvidenceJson = "{\"accepted\":true}",
            IsRequired = definition.Component.IsRequired
        });
        dbContext.MetricSamples.Add(new MetricSample
        {
            ComponentId = definition.ComponentId,
            Name = "heartbeatDelay",
            Value = 0,
            Unit = "min",
            ObservedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Accepted();
    }

    private static Dictionary<string, string[]> Validate(CreateHeartbeatRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!request.ComponentId.HasValue) errors["componentId"] = ["Informe o componente."];
        if (!IsValidText(request.Name, 160)) errors["name"] = ["Informe um nome válido com até 160 caracteres."];
        if (request.JobKey is not null && !IsValidJobKey(request.JobKey.Trim())) errors["jobKey"] = ["Use de 8 a 80 caracteres: letras, números, hífen ou sublinhado."];
        if (request.ExpectedIntervalSeconds is < 10 or > 86_400) errors["expectedIntervalSeconds"] = ["O intervalo deve estar entre 10 e 86400 segundos."];
        if (request.ToleranceSeconds is < 0 or > 86_400) errors["toleranceSeconds"] = ["A tolerância deve estar entre 0 e 86400 segundos."];
        if (request.WindowStart.HasValue != request.WindowEnd.HasValue
            || request.WindowStart.HasValue && request.WindowStart == request.WindowEnd)
        {
            errors["window"] = ["Informe início e fim diferentes, ou deixe ambos vazios."];
        }

        return errors;
    }

    private static bool IsValidText(string? value, int maximumLength)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= maximumLength && !trimmed.Any(char.IsControl);
    }

    private static bool IsValidJobKey(string value) =>
        value.Length is >= 8 and <= 80 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string CreateJobKey() => $"job_{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18))}";

    private static string CreateToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool TokenMatches(string token, string? expectedHash)
    {
        if (string.IsNullOrEmpty(expectedHash) || expectedHash.Length != 64)
        {
            return false;
        }

        try
        {
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IResult Unauthorized() =>
        Results.Json(new { message = "Heartbeat não autorizado." }, statusCode: StatusCodes.Status401Unauthorized);

    private static object TokenResponse(HeartbeatDefinition definition, string token) => new
    {
        definition.Id,
        definition.JobKey,
        token,
        tokenShownOnce = true,
        warning = "Armazene o token agora; ele não poderá ser consultado novamente."
    };

    private static void AddAudit(
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        string action,
        Guid entityId,
        object details)
    {
        var userClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = Guid.TryParse(userClaim, out var userId) ? userId : null,
            Action = action,
            EntityType = nameof(HeartbeatDefinition),
            EntityId = entityId.ToString(),
            SanitizedDetailsJson = JsonSerializer.Serialize(details),
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = clock.UtcNow
        });
    }

    public sealed record CreateHeartbeatRequest(
        Guid? ComponentId,
        string? Name,
        string? JobKey,
        int ExpectedIntervalSeconds,
        int ToleranceSeconds,
        TimeOnly? WindowStart,
        TimeOnly? WindowEnd);
}
