namespace ProtheusPulse.Domain.Monitoring;

/// <summary>
/// Métricas de uso percentual da máquina. O coletor grava com esses nomes e a regra de
/// alerta compara o limiar contra eles, então o nome precisa ser o mesmo dos dois lados.
/// </summary>
public static class ServerMetricNames
{
    public const string Cpu = "cpuUsage";
    public const string Memory = "memoryUsage";
    public const string Disk = "diskUsage";

    /// <summary>Métrica comparável por limiar, ou <c>null</c> para verificações que só têm estado.</summary>
    public static string? ForProbe(ProbeType probeType) => probeType switch
    {
        ProbeType.ServerCpu => Cpu,
        ProbeType.ServerMemory => Memory,
        ProbeType.ServerDisk => Disk,
        _ => null
    };
}
