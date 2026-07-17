namespace ProtheusPulse.Service.Configuration;

public sealed class PulseOptions
{
    public const string SectionName = "Pulse";
    public bool DemoMode { get; set; }
    public string? DataDirectory { get; set; }
    public int HistoryRetentionDays { get; set; } = 30;
    public int MetricAggregationAfterDays { get; set; } = 7;
    public int CollectionIntervalSeconds { get; set; } = 30;
    public int CollectorTimeoutSeconds { get; set; } = 10;
    public int MaximumConcurrentCollectors { get; set; } = 4;
    public int MaximumLogBytesPerCycle { get; set; } = 262_144;
    public double DiskWarningPercent { get; set; } = 15;
    public double DiskCriticalPercent { get; set; } = 5;
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    public string JwtIssuer { get; set; } = "ProtheusPulse";
    public string JwtAudience { get; set; } = "ProtheusPulse.Dashboard";
    public string? JwtSigningKey { get; set; }
    public int TokenLifetimeMinutes { get; set; } = 480;
}
