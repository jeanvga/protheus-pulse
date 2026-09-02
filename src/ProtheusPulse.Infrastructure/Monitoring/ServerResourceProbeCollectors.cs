using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Infrastructure.Monitoring;

/// <summary>
/// Traz processador, memória e discos da máquina para dentro do mesmo caminho dos demais
/// coletores. Sem isso a aba Servidor pintava o número de vermelho e mais nada acontecia:
/// não havia probe, então não havia regra, ocorrência, e-mail nem webhook.
/// </summary>
/// <remarks>
/// A leitura vem do <see cref="IServerResourceMonitor"/>, que o <c>ServerResourceWorker</c>
/// já amostra a cada poucos segundos. Consultar o sistema operacional de novo aqui só
/// atrapalharia: o uso de CPU é a diferença entre duas leituras consecutivas.
/// </remarks>
public abstract class ServerResourceProbeCollector(IClock clock, IServerResourceMonitor monitor) : IProbeCollector
{
    public abstract ProbeType Type { get; }

    /// <summary>Nome da métrica em percentual de uso, comparada pelo limiar da regra.</summary>
    public abstract string MetricName { get; }

    public bool CanCollect(Component component) => component.Installation?.IsSystem == true;

    public Task<ProbeObservation> CollectAsync(Component component, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var reading = Read(monitor.GetSnapshot());
        stopwatch.Stop();
        return Task.FromResult(new ProbeObservation(
            reading.Status,
            clock.UtcNow,
            stopwatch.Elapsed,
            reading.Message,
            reading.Evidence,
            IsRequired: true,
            reading.UsagePercent is { } usage
                ? [new MetricObservation(MetricName, Math.Round(usage, 1), "%")]
                : null));
    }

    protected abstract Reading Read(ServerResourceSnapshot snapshot);

    protected static string Percent(double value) => value.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"));

    protected sealed record Reading(HealthStatus Status, string Message, string? Evidence, double? UsagePercent);
}

public sealed class ServerCpuProbeCollector(IClock clock, IServerResourceMonitor monitor)
    : ServerResourceProbeCollector(clock, monitor)
{
    public override ProbeType Type => ProbeType.ServerCpu;

    public override string MetricName => ServerMetricNames.Cpu;

    protected override Reading Read(ServerResourceSnapshot snapshot)
    {
        if (snapshot.CpuUsagePercent is not { } usage)
        {
            return new Reading(HealthStatus.Unknown, snapshot.Notice ?? "Não foi possível ler o uso de processador.", null, null);
        }

        var evidence = JsonSerializer.Serialize(new { usagePercent = Math.Round(usage, 1), snapshot.ProcessorCount });
        return new Reading(snapshot.CpuStatus, $"Processador em {Percent(usage)}% com {snapshot.ProcessorCount} núcleos.", evidence, usage);
    }
}

public sealed class ServerMemoryProbeCollector(IClock clock, IServerResourceMonitor monitor)
    : ServerResourceProbeCollector(clock, monitor)
{
    private const double Gigabyte = 1024d * 1024 * 1024;

    public override ProbeType Type => ProbeType.ServerMemory;

    public override string MetricName => ServerMetricNames.Memory;

    protected override Reading Read(ServerResourceSnapshot snapshot)
    {
        if (snapshot.Memory is not { } memory)
        {
            return new Reading(HealthStatus.Unknown, snapshot.Notice ?? "Não foi possível ler o uso de memória.", null, null);
        }

        var evidence = JsonSerializer.Serialize(new
        {
            usagePercent = Math.Round(memory.UsedPercent, 1),
            totalBytes = memory.TotalBytes,
            availableBytes = memory.AvailableBytes
        });
        var message = $"Memória em {Percent(memory.UsedPercent)}%: {Percent(memory.UsedBytes / Gigabyte)} GB de {Percent(memory.TotalBytes / Gigabyte)} GB em uso.";
        return new Reading(snapshot.MemoryStatus, message, evidence, memory.UsedPercent);
    }
}

public sealed class ServerDiskProbeCollector(IClock clock, IServerResourceMonitor monitor)
    : ServerResourceProbeCollector(clock, monitor)
{
    public override ProbeType Type => ProbeType.ServerDisk;

    public override string MetricName => ServerMetricNames.Disk;

    protected override Reading Read(ServerResourceSnapshot snapshot)
    {
        if (snapshot.Disks.Count == 0)
        {
            return new Reading(HealthStatus.Unknown, "Nenhum volume fixo foi encontrado na máquina.", null, null);
        }

        // O volume mais apertado decide: um disco cheio derruba o servidor mesmo com os outros vazios.
        var tightest = snapshot.Disks.MaxBy(item => item.UsedPercent)!;
        var status = HealthAggregator.Aggregate(snapshot.Disks.Select(item => (item.Status, true)));
        var evidence = JsonSerializer.Serialize(new
        {
            volumes = snapshot.Disks.Count,
            tightest = tightest.Name,
            usagePercent = Math.Round(tightest.UsedPercent, 1),
            freeBytes = tightest.FreeBytes
        });
        var message = snapshot.Disks.Count == 1
            ? $"Volume {tightest.Name} em {Percent(tightest.UsedPercent)}% de uso, com {Percent(tightest.FreePercent)}% livre."
            : $"Volume mais cheio: {tightest.Name} em {Percent(tightest.UsedPercent)}% de uso, com {Percent(tightest.FreePercent)}% livre, entre {snapshot.Disks.Count} volumes.";
        return new Reading(status, message, evidence, tightest.UsedPercent);
    }
}
