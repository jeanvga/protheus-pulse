using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Service.Endpoints;

public static class ServiceControlEndpoints
{
    private const string MaintenanceModeName = "Modo manutenção";
    private const string OwnServiceName = "ProtheusPulse";
    private static readonly string[] AllowedActions = ["start", "stop", "restart"];
    private static readonly TimeSpan ServiceActionTimeout = TimeSpan.FromSeconds(40);

    public static RouteGroupBuilder MapServiceControl(this RouteGroupBuilder api, bool demoMode)
    {
        api.MapPost("/components/{id:guid}/service/{action}", (
            Guid id,
            string action,
            PulseDbContext dbContext,
            IClock clock,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            ExecuteComponentActionAsync(id, action, demoMode, dbContext, clock, principal, httpContext, cancellationToken))
            .RequireAuthorization("Administrator");

        api.MapGet("/maintenance/status", GetMaintenanceStatusAsync).RequireAuthorization("Viewer");

        api.MapPost("/maintenance/enter", (
            MaintenanceRequest? request,
            PulseDbContext dbContext,
            IClock clock,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            EnterMaintenanceAsync(request, demoMode, dbContext, clock, principal, httpContext, cancellationToken))
            .RequireAuthorization("Administrator");

        api.MapPost("/maintenance/exit", (
            PulseDbContext dbContext,
            IClock clock,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            ExitMaintenanceAsync(demoMode, dbContext, clock, principal, httpContext, cancellationToken))
            .RequireAuthorization("Administrator");

        return api;
    }

    private static async Task<IResult> ExecuteComponentActionAsync(
        Guid id,
        string action,
        bool demoMode,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var normalizedAction = action.Trim().ToLowerInvariant();
        if (!AllowedActions.Contains(normalizedAction))
        {
            return Results.BadRequest(new { message = "Use start, stop ou restart." });
        }

        if (demoMode)
        {
            return Results.Conflict(new { message = "Ações de serviço ficam desabilitadas no modo demonstração." });
        }

        if (!OperatingSystem.IsWindows())
        {
            return Results.Conflict(new { message = "Ações de serviço estão disponíveis somente no Windows." });
        }

        var component = await dbContext.Components
            .Include(item => item.WindowsServiceTargets)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (component is null)
        {
            return Results.NotFound(new { message = "Componente não encontrado." });
        }

        if (component.IsDemo)
        {
            return Results.Conflict(new { message = "Componentes demonstrativos não executam ações reais." });
        }

        if (component.WindowsServiceTargets.Count == 0)
        {
            return Results.Conflict(new { message = "O componente não possui serviço Windows configurado." });
        }

        var results = new List<ServiceActionOutcome>();
        foreach (var target in component.WindowsServiceTargets)
        {
            results.Add(await Task.Run(() => ExecuteServiceAction(target.ServiceName, normalizedAction), cancellationToken));
        }

        AddAudit(dbContext, clock, principal, httpContext, "ServiceActionExecuted", nameof(Component), component.Id, new
        {
            action = normalizedAction,
            services = results.Select(item => new { item.ServiceName, item.Success, item.Status }).ToArray()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { results });
    }

    private static async Task<IResult> GetMaintenanceStatusAsync(
        PulseDbContext dbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var endsAt = await dbContext.MaintenanceWindows
            .AsNoTracking()
            .Where(item => item.Name == MaintenanceModeName && item.StartsAt <= now && item.EndsAt > now)
            .MaxAsync(item => (DateTimeOffset?)item.EndsAt, cancellationToken);
        return Results.Ok(new { active = endsAt.HasValue, endsAt });
    }

    private static async Task<IResult> EnterMaintenanceAsync(
        MaintenanceRequest? request,
        bool demoMode,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (demoMode)
        {
            return Results.Conflict(new { message = "O modo manutenção fica desabilitado na demonstração." });
        }

        if (!OperatingSystem.IsWindows())
        {
            return Results.Conflict(new { message = "O modo manutenção está disponível somente no Windows." });
        }

        var installationIds = await dbContext.Installations
            .AsNoTracking()
            .Where(item => !item.IsDemo)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        if (installationIds.Length == 0)
        {
            return Results.Conflict(new { message = "Nenhuma instalação real cadastrada para entrar em manutenção." });
        }

        var now = clock.UtcNow;
        var durationMinutes = Math.Clamp(request?.DurationMinutes ?? 120, 15, 10_080);
        var endsAt = now.AddMinutes(durationMinutes);
        MaintenanceWindow? firstWindow = null;
        foreach (var installationId in installationIds)
        {
            var window = new MaintenanceWindow
            {
                InstallationId = installationId,
                Name = MaintenanceModeName,
                StartsAt = now,
                EndsAt = endsAt,
                Reason = string.IsNullOrWhiteSpace(request?.Reason) ? "Modo manutenção ativado pelo painel." : request!.Reason!.Trim()
            };
            dbContext.MaintenanceWindows.Add(window);
            firstWindow ??= window;
        }

        var services = await ExecuteOnMonitoredServicesAsync(dbContext, "stop", cancellationToken);
        AddAudit(dbContext, clock, principal, httpContext, "MaintenanceModeEntered", nameof(MaintenanceWindow), firstWindow!.Id, new
        {
            durationMinutes,
            services = services.Select(item => new { item.ServiceName, item.Success, item.Status }).ToArray()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { endsAt, services });
    }

    private static async Task<IResult> ExitMaintenanceAsync(
        bool demoMode,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (demoMode)
        {
            return Results.Conflict(new { message = "O modo manutenção fica desabilitado na demonstração." });
        }

        if (!OperatingSystem.IsWindows())
        {
            return Results.Conflict(new { message = "O modo manutenção está disponível somente no Windows." });
        }

        var now = clock.UtcNow;
        var activeWindows = await dbContext.MaintenanceWindows
            .Where(item => item.Name == MaintenanceModeName && item.EndsAt > now)
            .ToListAsync(cancellationToken);
        foreach (var window in activeWindows)
        {
            window.EndsAt = now;
        }

        var services = await ExecuteOnMonitoredServicesAsync(dbContext, "start", cancellationToken);
        AddAudit(dbContext, clock, principal, httpContext, "MaintenanceModeExited", nameof(MaintenanceWindow), activeWindows.FirstOrDefault()?.Id ?? Guid.Empty, new
        {
            closedWindows = activeWindows.Count,
            services = services.Select(item => new { item.ServiceName, item.Success, item.Status }).ToArray()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { services });
    }

    private static async Task<List<ServiceActionOutcome>> ExecuteOnMonitoredServicesAsync(
        PulseDbContext dbContext,
        string action,
        CancellationToken cancellationToken)
    {
        var serviceNames = await dbContext.Components
            .AsNoTracking()
            .Where(item => !item.IsDemo)
            .SelectMany(item => item.WindowsServiceTargets)
            .Select(item => item.ServiceName)
            .Distinct()
            .ToListAsync(cancellationToken);
        var results = new List<ServiceActionOutcome>();
        foreach (var serviceName in serviceNames)
        {
            if (string.Equals(serviceName, OwnServiceName, StringComparison.OrdinalIgnoreCase))
            {
                // O Pulse nunca para a si mesmo; sem ele o painel e a retomada deixariam de existir.
                continue;
            }

            results.Add(await Task.Run(() => ExecuteServiceAction(serviceName, action), cancellationToken));
        }

        return results;
    }

    private static ServiceActionOutcome ExecuteServiceAction(string serviceName, string action)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ServiceActionOutcome(serviceName, false, "Unsupported", "Ações de serviço estão disponíveis somente no Windows.");
        }

        try
        {
            using var controller = new ServiceController(serviceName);
            if (action is "stop" or "restart")
            {
                controller.Refresh();
                if (controller.Status != ServiceControllerStatus.Stopped)
                {
                    if (!controller.CanStop)
                    {
                        return new ServiceActionOutcome(serviceName, false, controller.Status.ToString(), "O serviço não permite parada no momento.");
                    }

                    controller.Stop();
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceActionTimeout);
                }
            }

            if (action is "start" or "restart")
            {
                controller.Refresh();
                if (controller.Status != ServiceControllerStatus.Running)
                {
                    controller.Start();
                    controller.WaitForStatus(ServiceControllerStatus.Running, ServiceActionTimeout);
                }
            }

            controller.Refresh();
            return new ServiceActionOutcome(serviceName, true, controller.Status.ToString(), "Ação concluída.");
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return new ServiceActionOutcome(serviceName, false, "Timeout", "O serviço não atingiu o estado esperado dentro do tempo limite.");
        }
        catch (InvalidOperationException exception)
        {
            var detail = exception.InnerException?.Message ?? exception.Message;
            return new ServiceActionOutcome(serviceName, false, "Error", detail);
        }
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

    public sealed record MaintenanceRequest(int? DurationMinutes, string? Reason);

    public sealed record ServiceActionOutcome(string ServiceName, bool Success, string Status, string Message);
}
