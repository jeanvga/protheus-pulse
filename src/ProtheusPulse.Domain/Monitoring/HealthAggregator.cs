namespace ProtheusPulse.Domain.Monitoring;

public static class HealthAggregator
{
    public static HealthStatus Aggregate(IEnumerable<(HealthStatus Status, bool IsRequired)> probes)
    {
        var materialized = probes.ToArray();
        if (materialized.Length == 0)
        {
            return HealthStatus.Unknown;
        }

        if (materialized.Any(static probe => probe.Status == HealthStatus.Maintenance))
        {
            return HealthStatus.Maintenance;
        }

        if (materialized.Any(static probe => probe.IsRequired && probe.Status == HealthStatus.Critical))
        {
            return HealthStatus.Critical;
        }

        if (materialized.Any(static probe => probe.Status is HealthStatus.Critical or HealthStatus.Warning))
        {
            return HealthStatus.Warning;
        }

        if (materialized.Any(static probe => probe.IsRequired && probe.Status == HealthStatus.Unknown))
        {
            return HealthStatus.Unknown;
        }

        return HealthStatus.Healthy;
    }
}
