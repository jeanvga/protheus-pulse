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

/// <summary>Como a conexão com o servidor SMTP é protegida.</summary>
public enum SmtpSecurity
{
    /// <summary>Escolhe STARTTLS ou TLS implícito conforme a porta e o anúncio do servidor.</summary>
    Auto,

    /// <summary>Sem criptografia; aceitável apenas em relay interno na mesma rede.</summary>
    None,

    /// <summary>Texto puro promovido a TLS pelo comando STARTTLS. Típico da porta 587.</summary>
    StartTls,

    /// <summary>TLS desde o handshake, sem texto puro. Típico da porta 465.</summary>
    SslOnConnect
}
