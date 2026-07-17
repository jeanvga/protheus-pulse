using ProtheusPulse.Service.Monitoring;

namespace ProtheusPulse.Service.HostedServices;

public sealed partial class RetentionWorker(
    RetentionService retentionService,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        do
        {
            try
            {
                var result = await retentionService.RunAsync(stoppingToken);
                LogRetentionCompleted(
                    logger,
                    result.DetailedMetricsAggregated,
                    result.ProbeResultsDeleted,
                    result.LogEventsDeleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogRetentionFailure(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Information,
        Message = "Retenção concluída: {AggregatedMetrics} métricas agregadas, {DeletedProbes} probes e {DeletedLogs} eventos de log removidos.")]
    private static partial void LogRetentionCompleted(ILogger logger, int aggregatedMetrics, int deletedProbes, int deletedLogs);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Error, Message = "Falha no job de retenção.")]
    private static partial void LogRetentionFailure(ILogger logger, Exception exception);
}
