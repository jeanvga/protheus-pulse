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

    /// <summary>Intervalo do watchdog que religa serviços das instalações com auto-start.</summary>
    public int AutoStartIntervalSeconds { get; set; } = 60;

    public int MaximumConcurrentCollectors { get; set; } = 4;
    public int MaximumLogBytesPerCycle { get; set; } = 262_144;
    public double DiskWarningPercent { get; set; } = 15;
    public double DiskCriticalPercent { get; set; } = 5;

    /// <summary>Intervalo entre as amostras de processador, memória e disco do servidor.</summary>
    public int ServerSampleIntervalSeconds { get; set; } = 5;

    /// <summary>Amostras mantidas em memória para o gráfico da aba Servidor.</summary>
    public int ServerHistorySamples { get; set; } = 120;

    public double CpuWarningPercent { get; set; } = 80;
    public double CpuCriticalPercent { get; set; } = 92;
    public double MemoryWarningPercent { get; set; } = 85;
    public double MemoryCriticalPercent { get; set; } = 94;

    /// <summary>Janela de agrupamento dos erros recebidos dos agentes antes de disparar o e-mail.</summary>
    public int LogAlertDigestSeconds { get; set; } = 120;
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    public string JwtIssuer { get; set; } = "ProtheusPulse";
    public string JwtAudience { get; set; } = "ProtheusPulse.Dashboard";
    public string? JwtSigningKey { get; set; }
    public int TokenLifetimeMinutes { get; set; } = 480;
}
