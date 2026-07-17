using ProtheusPulse.Application.Dashboard;
using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string password, string encodedHash);
}

public interface IDashboardQuery
{
    Task<DashboardSummary> GetSummaryAsync(bool demoMode, CancellationToken cancellationToken);
    Task<IReadOnlyList<InstallationListItem>> GetInstallationsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ComponentListItem>> GetComponentsAsync(CancellationToken cancellationToken);
}

public interface IDemoDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}

public interface IProbeCollector
{
    ProbeType Type { get; }
    bool CanCollect(Component component);
    Task<ProbeObservation> CollectAsync(Component component, CancellationToken cancellationToken);
}

public interface IIncrementalLogCollector
{
    bool CanCollect(Component component);
    Task<LogCollectionResult> CollectAsync(Component component, CancellationToken cancellationToken);
}

public sealed record ProbeObservation(
    HealthStatus Status,
    DateTimeOffset ObservedAt,
    TimeSpan Duration,
    string Message,
    string? SanitizedEvidence,
    bool IsRequired = true,
    IReadOnlyList<MetricObservation>? Metrics = null);

public sealed record MetricObservation(string Name, double Value, string Unit);

public sealed record LogEventObservation(
    Guid LogSourceId,
    DateTimeOffset ObservedAt,
    string Level,
    string Message,
    string Fingerprint,
    int OccurrenceCount);

public sealed record LogCollectionResult(
    ProbeObservation Observation,
    IReadOnlyList<LogEventObservation> Events);

public sealed class ProbeCollectorOptions
{
    public int MaximumLogBytesPerCycle { get; init; } = 262_144;
    public double DiskWarningPercent { get; init; } = 15;
    public double DiskCriticalPercent { get; init; } = 5;
}
