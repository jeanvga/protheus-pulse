using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Monitoring;

namespace ProtheusPulse.Service.Endpoints;

public static class OperationsEndpoints
{
    public static RouteGroupBuilder MapOperations(this RouteGroupBuilder api)
    {
        api.MapGet("/alert-rules", GetRulesAsync).RequireAuthorization("Viewer");
        api.MapPost("/alert-rules", CreateRuleAsync).RequireAuthorization("Administrator");
        api.MapPut("/alert-rules/{id:guid}/enabled", SetRuleEnabledAsync).RequireAuthorization("Administrator");
        api.MapPost("/alerts/{id:guid}/acknowledge", AcknowledgeAlertAsync).RequireAuthorization("Operator");

        api.MapPost("/maintenance-windows", CreateMaintenanceAsync).RequireAuthorization("Administrator");
        api.MapDelete("/maintenance-windows/{id:guid}", DeleteMaintenanceAsync).RequireAuthorization("Administrator");

        api.MapGet("/notification-channels", GetChannelsAsync).RequireAuthorization("Administrator");
        api.MapPost("/notification-channels", CreateChannelAsync).RequireAuthorization("Administrator");
        api.MapPut("/notification-channels/{id:guid}/enabled", SetChannelEnabledAsync).RequireAuthorization("Administrator");
        api.MapDelete("/notification-channels/{id:guid}", DeleteChannelAsync).RequireAuthorization("Administrator");
        api.MapPost("/maintenance/retention/run", async (RetentionService retentionService, CancellationToken cancellationToken) =>
            Results.Ok(await retentionService.RunAsync(cancellationToken))).RequireAuthorization("Administrator");
        return api;
    }

    private static async Task<IResult> GetRulesAsync(PulseDbContext dbContext, CancellationToken cancellationToken) =>
        Results.Ok(await dbContext.AlertRules.AsNoTracking()
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
                item.ProbeType,
                item.Severity,
                item.Enabled,
                item.MinimumConsecutiveFailures,
                item.CooldownSeconds
            })
            .ToListAsync(cancellationToken));

    private static async Task<IResult> CreateRuleAsync(
        CreateAlertRuleRequest request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidateRule(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (!await dbContext.Components.AnyAsync(item => item.Id == request.ComponentId, cancellationToken))
        {
            return Results.NotFound(new { message = "Componente não encontrado." });
        }

        var rule = new AlertRule
        {
            ComponentId = request.ComponentId!.Value,
            Name = request.Name!.Trim(),
            RuleKey = $"CUSTOM-{Guid.NewGuid():N}",
            ProbeType = request.ProbeType!.Value,
            Severity = request.Severity!.Value,
            Enabled = true,
            MinimumConsecutiveFailures = request.MinimumConsecutiveFailures,
            CooldownSeconds = request.CooldownSeconds,
            ConfigurationJson = JsonSerializer.Serialize(new
            {
                triggerStatuses = request.TriggerStatuses!.Select(item => item.ToString()).ToArray()
            })
        };
        dbContext.AlertRules.Add(rule);
        AddAudit(dbContext, clock, principal, httpContext, "AlertRuleCreated", nameof(AlertRule), rule.Id, new
        {
            rule.ProbeType,
            rule.Severity,
            rule.MinimumConsecutiveFailures,
            rule.CooldownSeconds
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/alert-rules/{rule.Id}", new { rule.Id });
    }

    private static async Task<IResult> SetRuleEnabledAsync(
        Guid id,
        EnabledRequest request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var rule = await dbContext.AlertRules.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        rule.Enabled = request.Enabled;
        AddAudit(dbContext, clock, principal, httpContext, "AlertRuleStateChanged", nameof(AlertRule), rule.Id, new { request.Enabled });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> AcknowledgeAlertAsync(
        Guid id,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var occurrence = await dbContext.AlertOccurrences.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (occurrence is null)
        {
            return Results.NotFound();
        }

        if (occurrence.State != AlertState.Active)
        {
            return Results.Conflict(new { message = "Somente alertas ativos podem ser reconhecidos." });
        }

        occurrence.State = AlertState.Acknowledged;
        occurrence.AcknowledgedAt = clock.UtcNow;
        AddAudit(dbContext, clock, principal, httpContext, "AlertAcknowledged", nameof(AlertOccurrence), occurrence.Id, new
        {
            occurrence.CorrelationId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateMaintenanceAsync(
        CreateMaintenanceRequest request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidateMaintenance(request, clock.UtcNow);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var targetExists = request.ComponentId.HasValue
            ? await dbContext.Components.AnyAsync(item => item.Id == request.ComponentId, cancellationToken)
            : await dbContext.Installations.AnyAsync(item => item.Id == request.InstallationId, cancellationToken);
        if (!targetExists)
        {
            return Results.NotFound(new { message = "Alvo da manutenção não encontrado." });
        }

        var window = new MaintenanceWindow
        {
            InstallationId = request.InstallationId,
            ComponentId = request.ComponentId,
            Name = request.Name!.Trim(),
            StartsAt = request.StartsAt!.Value,
            EndsAt = request.EndsAt!.Value,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
        };
        dbContext.MaintenanceWindows.Add(window);
        AddAudit(dbContext, clock, principal, httpContext, "MaintenanceWindowCreated", nameof(MaintenanceWindow), window.Id, new
        {
            target = request.ComponentId.HasValue ? "component" : "installation",
            durationMinutes = (int)(window.EndsAt - window.StartsAt).TotalMinutes
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/maintenance-windows/{window.Id}", new { window.Id });
    }

    private static async Task<IResult> DeleteMaintenanceAsync(
        Guid id,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var window = await dbContext.MaintenanceWindows.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (window is null)
        {
            return Results.NotFound();
        }

        dbContext.MaintenanceWindows.Remove(window);
        AddAudit(dbContext, clock, principal, httpContext, "MaintenanceWindowDeleted", nameof(MaintenanceWindow), window.Id, new { });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetChannelsAsync(PulseDbContext dbContext, CancellationToken cancellationToken) =>
        Results.Ok(await dbContext.NotificationChannels.AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new { item.Id, item.Name, item.Type, item.Enabled, Configured = item.ProtectedConfiguration != string.Empty })
            .ToListAsync(cancellationToken));

    private static async Task<IResult> CreateChannelAsync(
        CreateNotificationChannelRequest request,
        PulseDbContext dbContext,
        NotificationConfigurationProtector protector,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidateChannel(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var channel = new NotificationChannel
        {
            Name = request.Name!.Trim(),
            Type = request.Type!.Value,
            Enabled = request.Enabled,
            ProtectedConfiguration = protector.Protect(new NotificationChannelConfiguration(request.Url!.Trim()))
        };
        dbContext.NotificationChannels.Add(channel);
        AddAudit(dbContext, clock, principal, httpContext, "NotificationChannelCreated", nameof(NotificationChannel), channel.Id, new
        {
            channel.Type,
            channel.Enabled
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/notification-channels/{channel.Id}", new { channel.Id });
    }

    private static async Task<IResult> SetChannelEnabledAsync(
        Guid id,
        EnabledRequest request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var channel = await dbContext.NotificationChannels.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (channel is null)
        {
            return Results.NotFound();
        }

        channel.Enabled = request.Enabled;
        AddAudit(dbContext, clock, principal, httpContext, "NotificationChannelStateChanged", nameof(NotificationChannel), channel.Id, new { request.Enabled });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteChannelAsync(
        Guid id,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var channel = await dbContext.NotificationChannels.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (channel is null)
        {
            return Results.NotFound();
        }

        dbContext.NotificationChannels.Remove(channel);
        AddAudit(dbContext, clock, principal, httpContext, "NotificationChannelDeleted", nameof(NotificationChannel), channel.Id, new { channel.Type });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static Dictionary<string, string[]> ValidateRule(CreateAlertRuleRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!IsValidText(request.Name, 200)) errors["name"] = ["Informe um nome válido com até 200 caracteres."];
        if (!request.ComponentId.HasValue) errors["componentId"] = ["Informe o componente."];
        if (!request.ProbeType.HasValue || !Enum.IsDefined(request.ProbeType.Value)) errors["probeType"] = ["Informe um tipo de probe válido."];
        if (!request.Severity.HasValue || !Enum.IsDefined(request.Severity.Value)) errors["severity"] = ["Informe uma severidade válida."];
        if (request.MinimumConsecutiveFailures is < 1 or > 20) errors["minimumConsecutiveFailures"] = ["O valor deve estar entre 1 e 20."];
        if (request.CooldownSeconds is < 0 or > 86_400) errors["cooldownSeconds"] = ["O cooldown deve estar entre 0 e 86400 segundos."];
        if (request.TriggerStatuses is null || request.TriggerStatuses.Count == 0
            || request.TriggerStatuses.Any(item => item is HealthStatus.Healthy or HealthStatus.Maintenance || !Enum.IsDefined(item)))
        {
            errors["triggerStatuses"] = ["Informe ao menos um estado de falha válido."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateMaintenance(CreateMaintenanceRequest request, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!IsValidText(request.Name, 160)) errors["name"] = ["Informe um nome válido com até 160 caracteres."];
        if (request.InstallationId.HasValue == request.ComponentId.HasValue) errors["target"] = ["Informe exatamente uma instalação ou um componente."];
        if (!request.StartsAt.HasValue || !request.EndsAt.HasValue || request.EndsAt <= request.StartsAt) errors["period"] = ["Informe um período válido."];
        else if (request.EndsAt <= now || request.EndsAt - request.StartsAt > TimeSpan.FromDays(90)) errors["period"] = ["A janela deve terminar no futuro e durar no máximo 90 dias."];
        if (request.Reason is not null && !IsValidText(request.Reason, 500)) errors["reason"] = ["O motivo deve possuir até 500 caracteres sem controles."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidateChannel(CreateNotificationChannelRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!IsValidText(request.Name, 160)) errors["name"] = ["Informe um nome válido com até 160 caracteres."];
        if (!request.Type.HasValue || request.Type is NotificationChannelType.Dashboard or NotificationChannelType.Smtp || !Enum.IsDefined(request.Type.Value))
        {
            errors["type"] = ["Use Webhook, Teams, Slack ou Discord."];
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (request.Url?.Length ?? 0) > 2_048)
        {
            errors["url"] = ["Informe uma URL HTTPS absoluta, sem credenciais ou fragmento."];
        }

        return errors;
    }

    private static bool IsValidText(string? value, int maximumLength)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= maximumLength && !trimmed.Any(char.IsControl);
    }

    private static void AddAudit(
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        string action,
        string entityType,
        Guid entityId,
        object details)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = GetUserId(principal),
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            SanitizedDetailsJson = JsonSerializer.Serialize(details),
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = clock.UtcNow
        });
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public sealed record CreateAlertRuleRequest(
        Guid? ComponentId,
        string? Name,
        ProbeType? ProbeType,
        AlertSeverity? Severity,
        int MinimumConsecutiveFailures,
        int CooldownSeconds,
        IReadOnlyList<HealthStatus>? TriggerStatuses);

    public sealed record CreateMaintenanceRequest(
        Guid? InstallationId,
        Guid? ComponentId,
        string? Name,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        string? Reason);

    public sealed record CreateNotificationChannelRequest(
        string? Name,
        NotificationChannelType? Type,
        string? Url,
        bool Enabled = true);

    public sealed record EnabledRequest(bool Enabled);
}
