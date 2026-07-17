namespace ProtheusPulse.Domain.Monitoring;

public enum HealthStatus
{
    Healthy,
    Warning,
    Critical,
    Unknown,
    Maintenance
}

public enum EnvironmentKind
{
    Production,
    Homologation,
    Development,
    Custom
}

public enum ComponentType
{
    AppServer,
    Broker,
    Worker,
    Rest,
    WebApp,
    DbAccess,
    LicenseServer,
    Tss,
    Job,
    HttpEndpoint,
    WindowsService,
    Generic
}

public enum FileTargetKind
{
    Executable,
    Ini,
    Log,
    Directory,
    Generic
}

public enum ProbeType
{
    WindowsService,
    Process,
    Tcp,
    Http,
    TlsCertificate,
    File,
    Disk,
    Log,
    Heartbeat,
    Internal
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum AlertState
{
    Active,
    Acknowledged,
    Resolved,
    Silenced
}

public enum UserRole
{
    Administrator,
    Operator,
    Viewer
}

public enum NotificationChannelType
{
    Dashboard,
    Smtp,
    Webhook,
    Teams,
    Slack,
    Discord
}
