using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Configuration;
using ProtheusPulse.Service.Hubs;
using ProtheusPulse.Service.Monitoring;
using ProtheusPulse.Service.Observability;

namespace ProtheusPulse.Service.HostedServices;

public sealed partial class MonitoringWorker(
    IServiceScopeFactory scopeFactory,
    IHubContext<PulseHub> hubContext,
    IClock clock,
    PulseOptions options,
    LogAlertMailBuffer mailBuffer,
    PulseTelemetry telemetry,
    DatabaseReadyState readyState,
    ILogger<MonitoringWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim cycleGate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // O esquema pode ainda estar migrando: consultar antes disso só produziria erro.
        await readyState.WaitAsync(stoppingToken);
        await RunCycleSafelyAsync(stoppingToken);
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.CollectionIntervalSeconds, 10, 3_600));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleSafelyAsync(stoppingToken);
        }
    }

    public async Task<int> RunNowAsync(CancellationToken cancellationToken)
    {
        await cycleGate.WaitAsync(cancellationToken);
        var startedAt = Stopwatch.GetTimestamp();
        var updated = 0;
        var success = false;
        try
        {
            updated = await RunCycleAsync(cancellationToken);
            if (updated > 0)
            {
                await hubContext.Clients.All.SendAsync("dashboardUpdated", new { at = clock.UtcNow }, cancellationToken);
            }

            success = true;
            return updated;
        }
        finally
        {
            telemetry.RecordCollectionCycle(success, updated, Stopwatch.GetElapsedTime(startedAt));
            cycleGate.Release();
        }
    }

    private async Task RunCycleSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunNowAsync(cancellationToken);
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
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var componentIds = await dbContext.Components
            .AsNoTracking()
            .Where(item => !item.IsDemo)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var updated = 0;
        await Parallel.ForEachAsync(componentIds, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Clamp(options.MaximumConcurrentCollectors, 1, 16)
        }, async (componentId, token) =>
        {
            try
            {
                if (await CollectComponentAsync(componentId, token))
                {
                    Interlocked.Increment(ref updated);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogComponentFailure(logger, componentId, exception);
            }
        });
        return updated;
    }

    private async Task<bool> CollectComponentAsync(Guid componentId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;
        var dbContext = serviceProvider.GetRequiredService<PulseDbContext>();
        var component = await dbContext.Components
            .Include(item => item.Installation)
            .Include(item => item.WindowsServiceTargets)
            .Include(item => item.ProcessTargets)
            .Include(item => item.FileTargets)
            .Include(item => item.TcpChecks)
            .Include(item => item.HttpChecks)
            .Include(item => item.LogSources)
            .Include(item => item.HeartbeatDefinitions)
            .SingleAsync(item => item.Id == componentId, cancellationToken);
        var observations = new List<(ProbeType Type, ProbeObservation Observation)>();
        foreach (var collector in serviceProvider.GetServices<IProbeCollector>().Where(item => item.CanCollect(component)))
        {
            observations.Add((collector.Type, await CollectSafelyAsync(collector, component, cancellationToken)));
        }

        var logCollector = serviceProvider.GetRequiredService<IIncrementalLogCollector>();
        if (logCollector.CanCollect(component))
        {
            var logResult = await CollectLogsSafelyAsync(logCollector, component, cancellationToken);
            observations.Add((ProbeType.Log, logResult.Observation));
            dbContext.LogEvents.AddRange(logResult.Events.Select(item => new LogEvent
            {
                ComponentId = component.Id,
                LogSourceId = item.LogSourceId,
                ObservedAt = item.ObservedAt,
                Level = item.Level,
                Message = item.Message,
                Fingerprint = item.Fingerprint,
                OccurrenceCount = item.OccurrenceCount,
                ThreadId = item.Context?.ThreadId,
                User = item.Context?.User,
                Computer = item.Context?.Computer,
                SourceFile = item.Context?.SourceFile,
                SourceLine = item.Context?.SourceLine,
                Environment = item.Context?.Environment,
                Company = item.Context?.Company,
                Module = item.Context?.Module,
                Routine = item.Context?.Routine,
                Detail = item.Context?.Detail
            }));
            foreach (var item in logResult.Events)
            {
                telemetry.RecordLogEvent(
                    component.Installation.Name,
                    component.Name,
                    item.Level,
                    item.OccurrenceCount);
            }
            QueueLogErrorsForEmail(component, logResult.Events);
        }

        foreach (var item in observations)
        {
            telemetry.RecordProbe(
                component.Installation.Name,
                component.Name,
                item.Type,
                item.Observation.Status,
                item.Observation.IsRequired,
                item.Observation.Duration);
            dbContext.ProbeResults.Add(new ProbeResult
            {
                ComponentId = component.Id,
                ProbeType = item.Type,
                Status = item.Observation.Status,
                ObservedAt = item.Observation.ObservedAt,
                DurationMs = Math.Max(0, (long)item.Observation.Duration.TotalMilliseconds),
                Message = item.Observation.Message,
                EvidenceJson = item.Observation.SanitizedEvidence,
                IsRequired = item.Observation.IsRequired
            });
            if (item.Observation.Metrics is not null)
            {
                dbContext.MetricSamples.AddRange(item.Observation.Metrics.Select(metric => new MetricSample
                {
                    ComponentId = component.Id,
                    Name = metric.Name,
                    Value = metric.Value,
                    Unit = metric.Unit,
                    ObservedAt = item.Observation.ObservedAt
                }));
            }
        }

        var now = clock.UtcNow;
        var maintenanceActive = await dbContext.MaintenanceWindows.AsNoTracking().AnyAsync(item =>
            item.StartsAt <= now
            && item.EndsAt > now
            && (item.ComponentId == component.Id || item.InstallationId == component.InstallationId), cancellationToken);
        var newStatus = maintenanceActive
            ? HealthStatus.Maintenance
            : HealthAggregator.Aggregate(observations.Select(item => (item.Observation.Status, item.Observation.IsRequired)));
        telemetry.RecordComponentHealth(component.Installation.Name, component.Name, newStatus);
        if (component.Status != newStatus)
        {
            component.Status = newStatus;
            component.LastStateChangeAt = clock.UtcNow;
        }

        var alertEngine = serviceProvider.GetRequiredService<AlertEngine>();
        var transitions = await alertEngine.EvaluateAsync(component, observations, maintenanceActive, cancellationToken);
        dbContext.MetricSamples.Add(new MetricSample
        {
            ComponentId = component.Id,
            Name = "availability",
            Value = newStatus switch
            {
                HealthStatus.Healthy => 100,
                HealthStatus.Warning => 50,
                HealthStatus.Maintenance => 100,
                _ => 0
            },
            Unit = "%",
            ObservedAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await serviceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(transitions, cancellationToken);
        return observations.Count > 0;
    }

    /// <summary>
    /// Erro e crítico encontrados no log entram na fila do resumo por e-mail. O
    /// alerta avisa que o componente caiu; o resumo diz o que o Protheus escreveu.
    /// </summary>
    private void QueueLogErrorsForEmail(Component component, IReadOnlyList<LogEventObservation> events)
    {
        foreach (var item in events.Where(item => item.Level is "Critical" or "Error"))
        {
            mailBuffer.Enqueue(new LogAlertNotice(
                component.Installation.Name,
                component.Name,
                item.Level,
                item.Message,
                item.Fingerprint,
                item.OccurrenceCount,
                item.ObservedAt));
        }
    }

    private async Task<ProbeObservation> CollectSafelyAsync(
        IProbeCollector collector,
        Component component,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.CollectorTimeoutSeconds, 1, 120)));
        try
        {
            return await collector.CollectAsync(component, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UnknownObservation("A coleta excedeu o tempo limite.", component.IsRequired);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return UnknownObservation("A coleta falhou de forma controlada.", component.IsRequired);
        }
    }

    private async Task<LogCollectionResult> CollectLogsSafelyAsync(
        IIncrementalLogCollector collector,
        Component component,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.CollectorTimeoutSeconds, 1, 120)));
        try
        {
            return await collector.CollectAsync(component, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LogCollectionResult(UnknownObservation("A leitura incremental excedeu o tempo limite.", component.IsRequired), []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new LogCollectionResult(UnknownObservation("A leitura incremental falhou de forma controlada.", component.IsRequired), []);
        }
    }

    private ProbeObservation UnknownObservation(string message, bool required) =>
        new(HealthStatus.Unknown, clock.UtcNow, TimeSpan.Zero, message, "{\"controlledFailure\":true}", required);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Falha no ciclo de monitoramento.")]
    private static partial void LogCycleFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning, Message = "Falha controlada ao coletar o componente {ComponentId}.")]
    private static partial void LogComponentFailure(ILogger logger, Guid componentId, Exception exception);
}
