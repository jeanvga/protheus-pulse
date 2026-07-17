using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Configuration;

namespace ProtheusPulse.Service.Monitoring;

public sealed class RetentionService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    PulseOptions options)
{
    private const int MaximumAggregationBatch = 50_000;

    public async Task<RetentionResult> RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var retentionCutoff = clock.UtcNow.AddDays(-Math.Clamp(options.HistoryRetentionDays, 1, 365));
        var aggregationCutoff = clock.UtcNow.AddDays(-Math.Clamp(options.MetricAggregationAfterDays, 1, options.HistoryRetentionDays));
        var detailedSamples = await dbContext.MetricSamples
            .Where(item => item.AggregationWindow == null
                && item.ObservedAt >= retentionCutoff
                && item.ObservedAt < aggregationCutoff)
            .OrderBy(item => item.ObservedAt)
            .Take(MaximumAggregationBatch)
            .ToListAsync(cancellationToken);
        var aggregated = detailedSamples
            .GroupBy(item => new
            {
                item.ComponentId,
                item.Name,
                item.Unit,
                Hour = new DateTimeOffset(
                    item.ObservedAt.Year,
                    item.ObservedAt.Month,
                    item.ObservedAt.Day,
                    item.ObservedAt.Hour,
                    0,
                    0,
                    TimeSpan.Zero)
            })
            .Select(group => new MetricSample
            {
                ComponentId = group.Key.ComponentId,
                Name = group.Key.Name,
                Unit = group.Key.Unit,
                Value = group.Average(item => item.Value),
                ObservedAt = group.Key.Hour,
                AggregationWindow = TimeSpan.FromHours(1)
            })
            .ToArray();
        if (detailedSamples.Count > 0)
        {
            dbContext.MetricSamples.RemoveRange(detailedSamples);
            dbContext.MetricSamples.AddRange(aggregated);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var deletedProbes = await dbContext.ProbeResults
            .Where(item => item.ObservedAt < retentionCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var deletedLogs = await dbContext.LogEvents
            .Where(item => item.ObservedAt < retentionCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var deletedMetrics = await dbContext.MetricSamples
            .Where(item => item.ObservedAt < retentionCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var deletedAlerts = await dbContext.AlertOccurrences
            .Where(item => item.State == AlertState.Resolved && item.ResolvedAt < retentionCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var deletedMaintenanceWindows = await dbContext.MaintenanceWindows
            .Where(item => item.EndsAt < retentionCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        return new RetentionResult(
            detailedSamples.Count,
            aggregated.Length,
            deletedProbes,
            deletedLogs,
            deletedMetrics,
            deletedAlerts,
            deletedMaintenanceWindows,
            clock.UtcNow);
    }
}

public sealed record RetentionResult(
    int DetailedMetricsAggregated,
    int HourlyMetricsCreated,
    int ProbeResultsDeleted,
    int LogEventsDeleted,
    int MetricsDeleted,
    int AlertsDeleted,
    int MaintenanceWindowsDeleted,
    DateTimeOffset CompletedAt);
