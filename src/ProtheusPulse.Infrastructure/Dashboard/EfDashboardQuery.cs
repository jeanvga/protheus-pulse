using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Application.Dashboard;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Infrastructure.Dashboard;

public sealed class EfDashboardQuery(PulseDbContext dbContext, IClock clock) : IDashboardQuery
{
    public async Task<DashboardSummary> GetSummaryAsync(bool demoMode, CancellationToken cancellationToken)
    {
        var components = await dbContext.Components
            .AsNoTracking()
            .Include(item => item.Installation)
            .Include(item => item.ProbeResults.OrderByDescending(probe => probe.ObservedAt).Take(1))
            .Include(item => item.MetricSamples.OrderByDescending(metric => metric.ObservedAt).Take(2))
            .AsSplitQuery()
            .OrderBy(item => item.Installation.Name)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);

        var availabilitySamples = await dbContext.MetricSamples
            .AsNoTracking()
            .Where(item => item.Name == "availability")
            .OrderByDescending(item => item.ObservedAt)
            .Take(1_000)
            .ToListAsync(cancellationToken);

        var alerts = await dbContext.AlertOccurrences
            .AsNoTracking()
            .Include(item => item.AlertRule)
            .ThenInclude(item => item.Component)
            .ThenInclude(item => item.Installation)
            .OrderBy(item => item.State == AlertState.Resolved)
            .ThenByDescending(item => item.StartedAt)
            .Take(8)
            .ToListAsync(cancellationToken);

        var installationCount = components.Select(item => item.InstallationId).Distinct().Count();
        var healthy = components.Count(item => item.Status == HealthStatus.Healthy);
        var warning = components.Count(item => item.Status == HealthStatus.Warning);
        var critical = components.Count(item => item.Status == HealthStatus.Critical);
        var unknown = components.Count(item => item.Status == HealthStatus.Unknown);
        var latestAvailability = availabilitySamples
            .GroupBy(metric => metric.ComponentId)
            .Select(metrics => metrics
                .OrderByDescending(metric => metric.ObservedAt)
                .Select(metric => metric.Value)
                .First())
            .ToArray();
        var available = latestAvailability.Length > 0
            ? latestAvailability.Average()
            : components.Count == 0 ? 0 : (healthy + (warning * 0.5)) * 100d / components.Count;

        var snapshots = components.Select(component =>
        {
            var probe = component.ProbeResults.OrderByDescending(item => item.ObservedAt).FirstOrDefault();
            var metric = component.MetricSamples
                .Where(item => !string.Equals(item.Name, "availability", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.ObservedAt)
                .FirstOrDefault();
            return new ComponentSnapshot(
                component.Id,
                component.InstallationId,
                component.Installation.Name,
                component.Installation.Environment,
                component.Name,
                component.Type,
                component.Status,
                component.LastStateChangeAt,
                probe?.Message ?? "Aguardando a primeira coleta.",
                metric is null ? null : TranslateMetric(metric.Name),
                metric?.Value,
                metric?.Unit,
                component.IsDemo);
        }).ToArray();

        var alertSnapshots = alerts.Select(item => new AlertSnapshot(
            item.Id,
            item.CorrelationId,
            item.AlertRule.Component.Installation.Name,
            item.AlertRule.Component.Name,
            item.AlertRule.Name,
            item.AlertRule.Severity,
            item.State,
            item.StartedAt,
            item.ResolvedAt,
            item.Evidence)).ToArray();

        var availability = BuildAvailability(availabilitySamples, available);
        var totals = new DashboardTotals(
            installationCount,
            components.Count,
            healthy,
            warning,
            critical,
            unknown,
            alerts.Count(item => item.State is AlertState.Active or AlertState.Acknowledged),
            Math.Round(available, 1));

        return new DashboardSummary(clock.UtcNow, demoMode, totals, snapshots, alertSnapshots, availability);
    }

    public async Task<IReadOnlyList<InstallationListItem>> GetInstallationsAsync(CancellationToken cancellationToken)
    {
        var installations = await dbContext.Installations.AsNoTracking().Include(item => item.Components).OrderBy(item => item.Name).ToListAsync(cancellationToken);
        return installations.Select(item => new InstallationListItem(
            item.Id,
            item.Name,
            item.Environment,
            item.IsDemo,
            item.Components.Count,
            AggregateComponents(item.Components))).ToArray();
    }

    public async Task<IReadOnlyList<ComponentListItem>> GetComponentsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Components
            .AsNoTracking()
            .OrderBy(item => item.Installation.Name)
            .ThenBy(item => item.Name)
            .Select(item => new ComponentListItem(
                item.Id,
                item.InstallationId,
                item.Installation.Name,
                item.Name,
                item.Type,
                item.Status,
                item.IsDemo,
                item.LastStateChangeAt))
            .ToListAsync(cancellationToken);
    }

    private static string TranslateMetric(string name) => name switch
    {
        "memory" => "Memória",
        "heartbeatDelay" => "Atraso",
        "certificateDays" => "Validade TLS",
        "latency" => "Latência",
        "errors" => "Erros agrupados",
        _ => name
    };

    private AvailabilityPoint[] BuildAvailability(IEnumerable<MetricSample> samples, double fallback)
    {
        var points = samples
            .GroupBy(item => new DateTimeOffset(item.ObservedAt.Year, item.ObservedAt.Month, item.ObservedAt.Day, item.ObservedAt.Hour, 0, 0, TimeSpan.Zero))
            .Select(group => new AvailabilityPoint(group.Key, Math.Round(group.Average(item => item.Value), 1)))
            .OrderBy(item => item.At)
            .TakeLast(12)
            .ToArray();

        if (points.Length > 1)
        {
            return points;
        }

        return Enumerable.Range(0, 12)
            .Select(index => new AvailabilityPoint(clock.UtcNow.AddHours(index - 11), Math.Round(Math.Clamp(fallback + Math.Sin(index) * 0.6, 0, 100), 1)))
            .ToArray();
    }

    private static HealthStatus AggregateComponents(IEnumerable<Component> components)
    {
        var statuses = components.Select(item => (item.Status, item.IsRequired));
        return HealthAggregator.Aggregate(statuses);
    }
}
