using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.HostedServices;

namespace ProtheusPulse.Service.Endpoints;

/// <summary>
/// Agentes externos de log — hoje o agente Python que acompanha o console.log do
/// AppServer. O token é exibido uma vez e guardado como hash, e tudo o que chega
/// é resaneado aqui: o agente é uma fonte, não uma autoridade.
/// </summary>
public static class LogAgentEndpoints
{
    private const string TokenHeader = "X-Pulse-Agent-Token";
    private const int MaximumEventsPerRequest = 200;
    private const int MaximumPathLength = 2_048;

    public static RouteGroupBuilder MapLogAgents(this RouteGroupBuilder api)
    {
        api.MapGet("/log-agents", GetAgentsAsync).RequireAuthorization("Viewer");
        api.MapPost("/log-agents", CreateAgentAsync).RequireAuthorization("Administrator");
        api.MapPost("/log-agents/{id:guid}/rotate", RotateTokenAsync).RequireAuthorization("Administrator");
        api.MapDelete("/log-agents/{id:guid}", DeleteAgentAsync).RequireAuthorization("Administrator");
        api.MapPost("/log-agents/{agentKey}/events", IngestAsync).AllowAnonymous().RequireRateLimiting("agentIngest");
        return api;
    }

    private static async Task<IResult> GetAgentsAsync(PulseDbContext dbContext, CancellationToken cancellationToken) =>
        Results.Ok(await dbContext.LogAgents.AsNoTracking()
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
                item.AgentKey,
                item.Enabled,
                item.CreatedAt,
                item.LastSeenAt,
                item.ReceivedEventCount
            })
            .ToListAsync(cancellationToken));

    private static async Task<IResult> CreateAgentAsync(
        CreateLogAgentRequest request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!request.ComponentId.HasValue) errors["componentId"] = ["Informe o componente."];
        if (!IsValidText(request.Name, 160)) errors["name"] = ["Informe um nome válido com até 160 caracteres."];
        if (request.LogPath is not null && !IsValidPath(request.LogPath)) errors["logPath"] = ["Informe um caminho de log válido."];
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (!await dbContext.Components.AnyAsync(item => item.Id == request.ComponentId, cancellationToken))
        {
            return Results.NotFound(new { message = "Componente não encontrado." });
        }

        var token = CreateToken();
        var agent = new LogAgent
        {
            ComponentId = request.ComponentId!.Value,
            Name = request.Name!.Trim(),
            AgentKey = $"agt_{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18))}",
            TokenHash = HashToken(token),
            Enabled = true,
            CreatedAt = clock.UtcNow
        };
        dbContext.LogAgents.Add(agent);

        var logPath = request.LogPath?.Trim();
        if (!string.IsNullOrEmpty(logPath))
        {
            await EnsureAgentLogSourceAsync(dbContext, agent.ComponentId, logPath, cancellationToken);
        }

        AddAudit(dbContext, clock, principal, httpContext, "LogAgentCreated", agent.Id, new
        {
            agent.ComponentId,
            hasLogPath = !string.IsNullOrEmpty(logPath)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/log-agents/{agent.Id}", TokenResponse(agent, token));
    }

    private static async Task<IResult> RotateTokenAsync(
        Guid id,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var agent = await dbContext.LogAgents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (agent is null)
        {
            return Results.NotFound();
        }

        var token = CreateToken();
        agent.TokenHash = HashToken(token);
        AddAudit(dbContext, clock, principal, httpContext, "LogAgentTokenRotated", agent.Id, new { agent.ComponentId });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(TokenResponse(agent, token));
    }

    private static async Task<IResult> DeleteAgentAsync(
        Guid id,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var agent = await dbContext.LogAgents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (agent is null)
        {
            return Results.NotFound();
        }

        dbContext.LogAgents.Remove(agent);
        AddAudit(dbContext, clock, principal, httpContext, "LogAgentDeleted", agent.Id, new { agent.ComponentId });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> IngestAsync(
        string agentKey,
        IngestLogEventsRequest request,
        PulseDbContext dbContext,
        IClock clock,
        LogAlertMailBuffer mailBuffer,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var suppliedToken = httpContext.Request.Headers[TokenHeader].ToString();
        if (!IsValidAgentKey(agentKey) || suppliedToken.Length is < 20 or > 256)
        {
            return Unauthorized();
        }

        var agent = await dbContext.LogAgents
            .Include(item => item.Component)
            .ThenInclude(item => item.Installation)
            .SingleOrDefaultAsync(item => item.AgentKey == agentKey, cancellationToken);
        if (agent is null || !TokenMatches(suppliedToken, agent.TokenHash))
        {
            return Unauthorized();
        }

        if (!agent.Enabled)
        {
            return Results.Conflict(new { message = "Agente desabilitado." });
        }

        var now = clock.UtcNow;
        var path = NormalizePath(request.Source, agentKey);
        var source = await EnsureAgentLogSourceAsync(dbContext, agent.ComponentId, path, cancellationToken);
        var grouped = GroupEvents(request.Events, now);
        foreach (var item in grouped)
        {
            dbContext.LogEvents.Add(new LogEvent
            {
                ComponentId = agent.ComponentId,
                LogSourceId = source.Id,
                ObservedAt = item.ObservedAt,
                Level = item.Level,
                Message = item.Message,
                Fingerprint = item.Fingerprint,
                OccurrenceCount = item.OccurrenceCount
            });
        }

        if (grouped.Count > 0)
        {
            dbContext.ProbeResults.Add(new ProbeResult
            {
                ComponentId = agent.ComponentId,
                ProbeType = ProbeType.Log,
                Status = WorstStatus(grouped),
                ObservedAt = now,
                DurationMs = 0,
                Message = $"Agente {agent.Name} enviou {grouped.Count} evento(s) de log.",
                EvidenceJson = JsonSerializer.Serialize(new { source = "agent", events = grouped.Count }),
                IsRequired = agent.Component.IsRequired
            });
        }

        agent.LastSeenAt = now;
        agent.ReceivedEventCount += grouped.Sum(item => item.OccurrenceCount);
        source.LastReadAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var queued = 0;
        foreach (var item in grouped.Where(item => item.Level is "Critical" or "Error"))
        {
            mailBuffer.Enqueue(new LogAlertNotice(
                agent.Component.Installation.Name,
                agent.Component.Name,
                item.Level,
                item.Message,
                item.OccurrenceCount,
                item.ObservedAt));
            queued++;
        }

        return Results.Json(
            new { accepted = true, stored = grouped.Count, queuedForEmail = queued },
            statusCode: StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// A origem alimentada pelo agente é marcada para que a leitura incremental do
    /// próprio Pulse não processe o mesmo arquivo em paralelo.
    /// </summary>
    private static async Task<LogSource> EnsureAgentLogSourceAsync(
        PulseDbContext dbContext,
        Guid componentId,
        string path,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.LogSources
            .FirstOrDefaultAsync(item => item.ComponentId == componentId && item.Path == path, cancellationToken);
        if (existing is not null)
        {
            existing.IsAgentManaged = true;
            return existing;
        }

        var created = new LogSource
        {
            ComponentId = componentId,
            Path = path,
            IsAgentManaged = true
        };
        dbContext.LogSources.Add(created);
        return created;
    }

    private static List<NormalizedEvent> GroupEvents(IReadOnlyList<IngestLogEvent?>? events, DateTimeOffset now)
    {
        var grouped = new Dictionary<string, NormalizedEvent>(StringComparer.Ordinal);
        foreach (var item in (events ?? []).Take(MaximumEventsPerRequest))
        {
            if (item is null)
            {
                continue;
            }

            var message = LogTextSanitizer.Sanitize(item.Message ?? string.Empty);
            if (message.Length == 0)
            {
                continue;
            }

            var level = NormalizeLevel(item.Level, message);
            var fingerprint = LogTextSanitizer.CreateFingerprint(message);
            var count = Math.Clamp(item.OccurrenceCount <= 0 ? 1 : item.OccurrenceCount, 1, 10_000);
            var observedAt = NormalizeTimestamp(item.ObservedAt, now);
            if (grouped.TryGetValue(fingerprint, out var existing))
            {
                grouped[fingerprint] = existing with
                {
                    OccurrenceCount = existing.OccurrenceCount + count,
                    ObservedAt = observedAt > existing.ObservedAt ? observedAt : existing.ObservedAt
                };
            }
            else
            {
                grouped[fingerprint] = new NormalizedEvent(level, message, fingerprint, count, observedAt);
            }
        }

        return [.. grouped.Values];
    }

    /// <summary>
    /// O nível vem do agente, mas só é aceito se for conhecido; caso contrário a
    /// classificação é refeita a partir da própria mensagem.
    /// </summary>
    private static string NormalizeLevel(string? level, string message) => level?.Trim().ToLowerInvariant() switch
    {
        "critical" or "fatal" => "Critical",
        "error" => "Error",
        "warning" or "warn" => "Warning",
        "information" or "info" => "Information",
        _ => LogTextSanitizer.DetectLevel(message)
    };

    /// <summary>
    /// Relógio do agente fora de faixa não pode reescrever a linha do tempo do Pulse.
    /// </summary>
    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset? observedAt, DateTimeOffset now)
    {
        if (!observedAt.HasValue)
        {
            return now;
        }

        var value = observedAt.Value.ToUniversalTime();
        return value < now.AddHours(-24) || value > now.AddMinutes(5) ? now : value;
    }

    private static HealthStatus WorstStatus(IReadOnlyList<NormalizedEvent> events)
    {
        if (events.Any(item => item.Level == "Critical"))
        {
            return HealthStatus.Critical;
        }

        return events.Any(item => item.Level is "Error" or "Warning") ? HealthStatus.Warning : HealthStatus.Healthy;
    }

    private static string NormalizePath(string? source, string agentKey)
    {
        var trimmed = source?.Trim();
        return string.IsNullOrEmpty(trimmed) || !IsValidPath(trimmed) ? $"agent://{agentKey}" : trimmed;
    }

    private static bool IsValidPath(string? value)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && trimmed.Length <= MaximumPathLength
            && !trimmed.Any(char.IsControl);
    }

    private static bool IsValidText(string? value, int maximumLength)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= maximumLength && !trimmed.Any(char.IsControl);
    }

    private static bool IsValidAgentKey(string value) =>
        value.Length is >= 8 and <= 80 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

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
        Results.Json(new { message = "Agente não autorizado." }, statusCode: StatusCodes.Status401Unauthorized);

    private static object TokenResponse(LogAgent agent, string token) => new
    {
        agent.Id,
        agent.AgentKey,
        token,
        tokenShownOnce = true,
        ingestUrl = string.Create(CultureInfo.InvariantCulture, $"/api/v1/log-agents/{agent.AgentKey}/events"),
        warning = "Guarde o token agora; ele não poderá ser consultado novamente."
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
            EntityType = nameof(LogAgent),
            EntityId = entityId.ToString(),
            SanitizedDetailsJson = JsonSerializer.Serialize(details),
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = clock.UtcNow
        });
    }

    private sealed record NormalizedEvent(
        string Level,
        string Message,
        string Fingerprint,
        int OccurrenceCount,
        DateTimeOffset ObservedAt);

    public sealed record CreateLogAgentRequest(Guid? ComponentId, string? Name, string? LogPath);

    public sealed record IngestLogEventsRequest(string? Source, IReadOnlyList<IngestLogEvent?>? Events);

    public sealed record IngestLogEvent(DateTimeOffset? ObservedAt, string? Level, string? Message, int OccurrenceCount);
}
