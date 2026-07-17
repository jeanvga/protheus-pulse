using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Application.Dashboard;

public sealed record DashboardSummary(
    DateTimeOffset GeneratedAt,
    bool DemoMode,
    DashboardTotals Totals,
    IReadOnlyList<ComponentSnapshot> Components,
    IReadOnlyList<AlertSnapshot> Alerts,
    IReadOnlyList<AvailabilityPoint> Availability);

public sealed record DashboardTotals(
    int Installations,
    int Components,
    int Healthy,
    int Warning,
    int Critical,
    int Unknown,
    int ActiveAlerts,
    double AvailabilityPercent);

public sealed record ComponentSnapshot(
    Guid Id,
    Guid InstallationId,
    string InstallationName,
    EnvironmentKind InstallationEnvironment,
    string Name,
    ComponentType Type,
    HealthStatus Status,
    DateTimeOffset? LastStateChangeAt,
    string Summary,
    string? MetricLabel,
    double? MetricValue,
    string? MetricUnit,
    bool IsDemo);

public sealed record AlertSnapshot(
    Guid Id,
    Guid CorrelationId,
    string InstallationName,
    string ComponentName,
    string RuleName,
    AlertSeverity Severity,
    AlertState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? ResolvedAt,
    string Evidence);

public sealed record AvailabilityPoint(DateTimeOffset At, double Value);

public sealed record InstallationListItem(
    Guid Id,
    string Name,
    EnvironmentKind Environment,
    bool IsDemo,
    int ComponentCount,
    HealthStatus Status);

public sealed record ComponentListItem(
    Guid Id,
    Guid InstallationId,
    string InstallationName,
    string Name,
    ComponentType Type,
    HealthStatus Status,
    bool IsDemo,
    DateTimeOffset? LastStateChangeAt);
