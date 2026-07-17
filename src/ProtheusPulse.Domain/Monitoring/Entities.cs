using ProtheusPulse.Domain.Common;

namespace ProtheusPulse.Domain.Monitoring;

public sealed class Installation : Entity
{
    public required string Name { get; set; }
    public EnvironmentKind Environment { get; set; }
    public string? CustomEnvironmentName { get; set; }
    public string TagsJson { get; set; } = "[]";
    public bool IsDemo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<Component> Components { get; set; } = new List<Component>();
}

public sealed class Component : Entity
{
    public Guid InstallationId { get; set; }
    public required string Name { get; set; }
    public ComponentType Type { get; set; }
    public HealthStatus Status { get; set; } = HealthStatus.Unknown;
    public bool IsRequired { get; set; } = true;
    public bool IsDemo { get; set; }
    public DateTimeOffset? LastStateChangeAt { get; set; }
    public Installation Installation { get; set; } = null!;
    public ICollection<WindowsServiceTarget> WindowsServiceTargets { get; set; } = new List<WindowsServiceTarget>();
    public ICollection<ProcessTarget> ProcessTargets { get; set; } = new List<ProcessTarget>();
    public ICollection<FileTarget> FileTargets { get; set; } = new List<FileTarget>();
    public ICollection<TcpCheck> TcpChecks { get; set; } = new List<TcpCheck>();
    public ICollection<HttpCheck> HttpChecks { get; set; } = new List<HttpCheck>();
    public ICollection<LogSource> LogSources { get; set; } = new List<LogSource>();
    public ICollection<HeartbeatDefinition> HeartbeatDefinitions { get; set; } = new List<HeartbeatDefinition>();
    public ICollection<ProbeResult> ProbeResults { get; set; } = new List<ProbeResult>();
    public ICollection<MetricSample> MetricSamples { get; set; } = new List<MetricSample>();
    public ICollection<AlertRule> AlertRules { get; set; } = new List<AlertRule>();
}

public sealed class WindowsServiceTarget : Entity
{
    public Guid ComponentId { get; set; }
    public required string ServiceName { get; set; }
    public string? DisplayName { get; set; }
    public Component Component { get; set; } = null!;
}

public sealed class ProcessTarget : Entity
{
    public Guid ComponentId { get; set; }
    public string? ExecutablePath { get; set; }
    public string? ExpectedFileVersion { get; set; }
    public Component Component { get; set; } = null!;
}

public sealed class FileTarget : Entity
{
    public Guid ComponentId { get; set; }
    public required string Path { get; set; }
    public FileTargetKind Kind { get; set; }
    public bool IsRequired { get; set; } = true;
    public Component Component { get; set; } = null!;
}

public sealed class TcpCheck : Entity
{
    public Guid ComponentId { get; set; }
    public required string Host { get; set; }
    public int Port { get; set; }
    public int TimeoutMs { get; set; } = 3_000;
    public bool IsRequired { get; set; } = true;
    public Component Component { get; set; } = null!;
}

public sealed class HttpCheck : Entity
{
    public Guid ComponentId { get; set; }
    public required string Url { get; set; }
    public string Method { get; set; } = "GET";
    public int ExpectedStatusMin { get; set; } = 200;
    public int ExpectedStatusMax { get; set; } = 399;
    public int TimeoutMs { get; set; } = 5_000;
    public string? BodyPattern { get; set; }
    public bool ValidateTls { get; set; } = true;
    public int CertificateWarningDays { get; set; } = 30;
    public bool IsRequired { get; set; } = true;
    public Component Component { get; set; } = null!;
}

public sealed class LogSource : Entity
{
    public Guid ComponentId { get; set; }
    public required string Path { get; set; }
    public string EncodingName { get; set; } = "auto";
    public long CursorOffset { get; set; }
    public string? FileIdentity { get; set; }
    public DateTimeOffset? LastReadAt { get; set; }
    public Component Component { get; set; } = null!;
}

public sealed class HeartbeatDefinition : Entity
{
    public Guid ComponentId { get; set; }
    public required string Name { get; set; }
    public required string JobKey { get; set; }
    public string? TokenHash { get; set; }
    public int ExpectedIntervalSeconds { get; set; }
    public int ToleranceSeconds { get; set; }
    public TimeOnly? WindowStart { get; set; }
    public TimeOnly? WindowEnd { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public Component Component { get; set; } = null!;
}

public sealed class ProbeResult : Entity
{
    public Guid ComponentId { get; set; }
    public ProbeType ProbeType { get; set; }
    public HealthStatus Status { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public long DurationMs { get; set; }
    public required string Message { get; set; }
    public string? EvidenceJson { get; set; }
    public bool IsRequired { get; set; } = true;
    public Component Component { get; set; } = null!;
}

public sealed class MetricSample : Entity
{
    public Guid ComponentId { get; set; }
    public required string Name { get; set; }
    public double Value { get; set; }
    public required string Unit { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public TimeSpan? AggregationWindow { get; set; }
    public Component Component { get; set; } = null!;
}

public sealed class AlertRule : Entity
{
    public Guid ComponentId { get; set; }
    public required string Name { get; set; }
    public required string RuleKey { get; set; }
    public ProbeType ProbeType { get; set; }
    public AlertSeverity Severity { get; set; }
    public bool Enabled { get; set; } = true;
    public int MinimumConsecutiveFailures { get; set; } = 2;
    public int CooldownSeconds { get; set; } = 300;
    public string ConfigurationJson { get; set; } = "{}";
    public Component Component { get; set; } = null!;
    public ICollection<AlertOccurrence> Occurrences { get; set; } = new List<AlertOccurrence>();
}

public sealed class AlertOccurrence : Entity
{
    public Guid AlertRuleId { get; set; }
    public AlertState State { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public required string Evidence { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public AlertRule AlertRule { get; set; } = null!;
}

public sealed class NotificationChannel : Entity
{
    public required string Name { get; set; }
    public NotificationChannelType Type { get; set; }
    public bool Enabled { get; set; }
    public string ProtectedConfiguration { get; set; } = string.Empty;
}

public sealed class MaintenanceWindow : Entity
{
    public Guid? InstallationId { get; set; }
    public Guid? ComponentId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string? Reason { get; set; }
    public Installation? Installation { get; set; }
    public Component? Component { get; set; }
}

public sealed class User : Entity
{
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class AuditEvent : Entity
{
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? SanitizedDetailsJson { get; set; }
    public string? RemoteAddress { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public User? User { get; set; }
}
