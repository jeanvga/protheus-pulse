using System.Diagnostics;
using System.Text.Json;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Infrastructure.Monitoring;

internal static class CollectorSupport
{
    public static ProbeObservation CreateObservation(
        Stopwatch stopwatch,
        IReadOnlyCollection<TargetObservation> observations,
        DateTimeOffset observedAt,
        string healthyMessage,
        string warningMessage,
        string criticalMessage,
        string unknownMessage,
        IReadOnlyList<MetricObservation>? metrics = null)
    {
        stopwatch.Stop();
        var status = HealthAggregator.Aggregate(observations.Select(item => (item.Status, item.IsRequired)));
        var message = status switch
        {
            HealthStatus.Healthy => healthyMessage,
            HealthStatus.Warning => warningMessage,
            HealthStatus.Critical => criticalMessage,
            _ => unknownMessage
        };
        var evidence = JsonSerializer.Serialize(new
        {
            total = observations.Count,
            healthy = observations.Count(item => item.Status == HealthStatus.Healthy),
            warning = observations.Count(item => item.Status == HealthStatus.Warning),
            critical = observations.Count(item => item.Status == HealthStatus.Critical),
            unknown = observations.Count(item => item.Status == HealthStatus.Unknown)
        });
        return new ProbeObservation(
            status,
            observedAt,
            stopwatch.Elapsed,
            message,
            evidence,
            observations.Any(item => item.IsRequired),
            metrics);
    }

    internal sealed record TargetObservation(HealthStatus Status, bool IsRequired);
}
