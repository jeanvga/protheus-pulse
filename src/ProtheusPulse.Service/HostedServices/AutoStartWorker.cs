using System.Collections.Concurrent;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Configuration;
using ProtheusPulse.Service.Hubs;

namespace ProtheusPulse.Service.HostedServices;

/// <summary>
/// Watchdog do auto-start: acompanha os serviços Windows das instalações marcadas
/// e sobe novamente as que caíram. Ambientes em manutenção ficam de fora, exceto a
/// instalação exclusiva, que precisa continuar no ar durante a janela.
/// </summary>
public sealed partial class AutoStartWorker(
    IServiceScopeFactory scopeFactory,
    IHubContext<PulseHub> hubContext,
    IClock clock,
    PulseOptions options,
    ILogger<AutoStartWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(40);
    private readonly ConcurrentDictionary<string, AutoStartAttempt> attempts = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.AutoStartIntervalSeconds, 15, 3_600));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleSafelyAsync(stoppingToken);
        }
    }

    private async Task RunCycleSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var recovered = await RunCycleAsync(cancellationToken);
            if (recovered > 0)
            {
                await hubContext.Clients.All.SendAsync("dashboardUpdated", new { at = clock.UtcNow }, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Encerramento normal do serviço.
        }
        catch (Exception exception)
        {
            LogCycleFailure(logger, exception);
        }
    }

    private async Task<int> RunCycleAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var now = clock.UtcNow;
        var eligibleIds = await GetEligibleInstallationIdsAsync(dbContext, now, cancellationToken);
        if (eligibleIds.Count == 0)
        {
            return 0;
        }

        var targets = await dbContext.WindowsServiceTargets
            .Include(item => item.Component)
            .Where(item => !item.Component.IsDemo && eligibleIds.Contains(item.Component.InstallationId))
            .ToListAsync(cancellationToken);
        var recovered = 0;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = ReadStatus(target.ServiceName);
            target.LastStatus = status;
            target.LastStatusAt = clock.UtcNow;
            if (ServiceStateRules.IsRunning(status))
            {
                attempts.TryRemove(target.ServiceName, out _);
                continue;
            }

            var attempt = attempts.TryGetValue(target.ServiceName, out var previous) ? previous : null;
            if (!AutoStartPolicy.ShouldAttempt(status, attempt, clock.UtcNow))
            {
                continue;
            }

            var registered = AutoStartPolicy.Register(attempt, clock.UtcNow);
            attempts[target.ServiceName] = registered;
            var outcome = await Task.Run(() => StartService(target.ServiceName), cancellationToken);
            target.LastStatus = outcome.Status;
            target.LastStatusAt = clock.UtcNow;
            if (outcome.Success)
            {
                attempts.TryRemove(target.ServiceName, out _);
                recovered++;
                LogRecovered(logger, target.ServiceName, registered.Count);
            }
            else
            {
                LogRecoveryFailed(logger, target.ServiceName, registered.Count, outcome.Status);
            }

            AddAudit(dbContext, target, outcome, registered.Count);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return recovered;
    }

    private static async Task<HashSet<Guid>> GetEligibleInstallationIdsAsync(
        PulseDbContext dbContext,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var installations = await dbContext.Installations
            .AsNoTracking()
            .Where(item => !item.IsDemo && item.AutoStartEnabled)
            .Select(item => new { item.Id, item.IsExclusive })
            .ToArrayAsync(cancellationToken);
        if (installations.Length == 0)
        {
            return [];
        }

        var suspended = await dbContext.MaintenanceWindows
            .AsNoTracking()
            .Where(item => item.InstallationId != null && item.StartsAt <= now && item.EndsAt > now)
            .Select(item => item.InstallationId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Durante a manutenção o watchdog só cuida da instalação exclusiva; religar
        // um ambiente suspenso derrubaria a própria janela de manutenção.
        return installations
            .Where(item => item.IsExclusive || !suspended.Contains(item.Id))
            .Select(item => item.Id)
            .ToHashSet();
    }

    private static string ReadStatus(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Unsupported";
        }

        try
        {
            using var controller = new ServiceController(serviceName);
            controller.Refresh();
            return controller.Status.ToString();
        }
        catch (InvalidOperationException)
        {
            return "NotFound";
        }
    }

    private static RecoveryOutcome StartService(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new RecoveryOutcome(false, "Unsupported", "Ações de serviço estão disponíveis somente no Windows.");
        }

        try
        {
            using var controller = new ServiceController(serviceName);
            controller.Refresh();
            if (controller.Status != ServiceControllerStatus.Running)
            {
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, RecoveryTimeout);
            }

            controller.Refresh();
            return new RecoveryOutcome(true, controller.Status.ToString(), "Serviço religado pelo auto-start.");
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return new RecoveryOutcome(false, "Timeout", "O serviço não subiu dentro do tempo limite.");
        }
        catch (InvalidOperationException exception)
        {
            var detail = exception.InnerException?.Message ?? exception.Message;
            return new RecoveryOutcome(false, "Error", detail);
        }
    }

    private void AddAudit(PulseDbContext dbContext, WindowsServiceTarget target, RecoveryOutcome outcome, int attempt)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = null,
            Action = outcome.Success ? "AutoStartRecovered" : "AutoStartFailed",
            EntityType = nameof(Component),
            EntityId = target.ComponentId.ToString(),
            SanitizedDetailsJson = JsonSerializer.Serialize(new
            {
                serviceName = target.ServiceName,
                attempt,
                status = outcome.Status,
                message = outcome.Message
            }),
            RemoteAddress = null,
            OccurredAt = clock.UtcNow
        });
    }

    private sealed record RecoveryOutcome(bool Success, string Status, string Message);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Error, Message = "Falha no ciclo do auto-start.")]
    private static partial void LogCycleFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Information, Message = "Auto-start religou o serviço {ServiceName} na tentativa {Attempt}.")]
    private static partial void LogRecovered(ILogger logger, string serviceName, int attempt);

    [LoggerMessage(EventId = 1503, Level = LogLevel.Warning, Message = "Auto-start não conseguiu religar {ServiceName} na tentativa {Attempt}: {Status}.")]
    private static partial void LogRecoveryFailed(ILogger logger, string serviceName, int attempt, string status);
}
