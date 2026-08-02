using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Application.Abstractions;

/// <summary>
/// Leitura somente leitura do processador, da memória e dos discos da máquina
/// onde o Pulse está instalado. A amostragem é periódica porque o uso de CPU só
/// existe como diferença entre duas leituras.
/// </summary>
public interface IServerResourceMonitor
{
    /// <summary>Faz uma leitura e a acrescenta ao histórico em memória.</summary>
    ServerResourceSnapshot Sample();

    /// <summary>Devolve a última leitura sem tocar no sistema operacional.</summary>
    ServerResourceSnapshot GetSnapshot();
}

public sealed record ServerResourceSnapshot(
    DateTimeOffset ObservedAt,
    string HostName,
    string OperatingSystem,
    int ProcessorCount,
    long UptimeSeconds,
    double? CpuUsagePercent,
    HealthStatus CpuStatus,
    ServerMemoryUsage? Memory,
    HealthStatus MemoryStatus,
    IReadOnlyList<ServerDiskUsage> Disks,
    IReadOnlyList<ServerResourceSample> History,
    string? Notice);

public sealed record ServerMemoryUsage(
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes,
    double UsedPercent);

public sealed record ServerDiskUsage(
    string Name,
    string? Label,
    string Format,
    long TotalBytes,
    long UsedBytes,
    long FreeBytes,
    double UsedPercent,
    double FreePercent,
    HealthStatus Status);

public sealed record ServerResourceSample(
    DateTimeOffset At,
    double? CpuPercent,
    double? MemoryPercent);

public sealed class ServerResourceOptions
{
    public double CpuWarningPercent { get; init; } = 80;
    public double CpuCriticalPercent { get; init; } = 92;
    public double MemoryWarningPercent { get; init; } = 85;
    public double MemoryCriticalPercent { get; init; } = 94;

    /// <summary>Percentual de espaço <em>livre</em> que ainda é apenas atenção.</summary>
    public double DiskWarningPercent { get; init; } = 15;

    /// <summary>Percentual de espaço <em>livre</em> considerado crítico.</summary>
    public double DiskCriticalPercent { get; init; } = 5;

    public int HistorySamples { get; init; } = 120;
}
