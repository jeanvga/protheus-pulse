namespace ProtheusPulse.Service.Configuration;

public sealed class InstallationImportDocument
{
    public int SchemaVersion { get; set; }
    public List<InstallationDefinition>? Installations { get; set; }
}

public sealed class InstallationDefinition
{
    public string? Name { get; set; }
    public string? Environment { get; set; }
    public string? CustomEnvironmentName { get; set; }
    public List<string?>? Tags { get; set; }
    public List<ComponentDefinition?>? Components { get; set; }
}

public sealed class ComponentDefinition
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool IsRequired { get; set; } = true;
    public string? WindowsServiceName { get; set; }
    public string? ExecutablePath { get; set; }
    public string? IniPath { get; set; }
    public List<string?>? LogPaths { get; set; }
    public List<TcpCheckDefinition?>? TcpChecks { get; set; }
    public List<HttpCheckDefinition?>? HttpChecks { get; set; }
}

public sealed class TcpCheckDefinition
{
    public string? Host { get; set; }
    public int Port { get; set; }
    public int TimeoutMs { get; set; } = 3_000;
    public bool IsRequired { get; set; } = true;
}

public sealed class HttpCheckDefinition
{
    public string? Url { get; set; }
    public string Method { get; set; } = "GET";
    public int ExpectedStatusMin { get; set; } = 200;
    public int ExpectedStatusMax { get; set; } = 399;
    public int TimeoutMs { get; set; } = 5_000;
    public string? BodyPattern { get; set; }
    public bool ValidateTls { get; set; } = true;
    public int CertificateWarningDays { get; set; } = 30;
    public bool IsRequired { get; set; } = true;
}
