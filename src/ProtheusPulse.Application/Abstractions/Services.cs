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
    Task<ProbeObservation> CollectAsync(Component component, CancellationToken cancellationToken);
}

public sealed record ProbeObservation(
    HealthStatus Status,
    DateTimeOffset ObservedAt,
    TimeSpan Duration,
    string Message,
    string? SanitizedEvidence);
