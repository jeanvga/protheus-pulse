using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Infrastructure.Monitoring;

/// <summary>
/// Lê processador, memória e discos do próprio servidor. O uso de CPU só existe
/// como diferença entre duas leituras do relógio do kernel, então o coletor guarda
/// a leitura anterior e precisa ser registrado como singleton.
/// </summary>
public sealed class ServerResourceCollector : IServerResourceMonitor
{
    private const string WindowsOnlyNotice = "A leitura de processador e memória usa APIs do Windows e fica indisponível nesta plataforma.";
    private const int MemoryStatusLength = 64;
    private const int TotalPhysicalOffset = 8;
    private const int AvailablePhysicalOffset = 16;

    private readonly IClock clock;
    private readonly ServerResourceOptions options;
    private readonly object gate = new();
    private readonly Queue<ServerResourceSample> history = new();
    private ProcessorTimes? previousProcessorTimes;
    private ServerResourceSnapshot? latest;

    public ServerResourceCollector(IClock clock, ServerResourceOptions options)
    {
        this.clock = clock;
        this.options = options;
        // Leitura de partida para que a primeira amostra periódica já tenha um
        // intervalo com que se comparar e devolva um percentual real.
        previousProcessorTimes = ReadProcessorTimes();
    }

    public ServerResourceSnapshot Sample()
    {
        lock (gate)
        {
            return SampleCore();
        }
    }

    public ServerResourceSnapshot GetSnapshot()
    {
        lock (gate)
        {
            return latest ?? SampleCore();
        }
    }

    private ServerResourceSnapshot SampleCore()
    {
        var cpuUsagePercent = ReadCpuUsagePercent();
        var memory = ReadMemory();
        var disks = ReadDisks();
        var observedAt = clock.UtcNow;
        history.Enqueue(new ServerResourceSample(observedAt, cpuUsagePercent, memory?.UsedPercent));
        while (history.Count > Math.Clamp(options.HistorySamples, 2, 2_880))
        {
            history.Dequeue();
        }

        latest = new ServerResourceSnapshot(
            observedAt,
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            Environment.TickCount64 / 1_000,
            cpuUsagePercent,
            Classify(cpuUsagePercent, options.CpuWarningPercent, options.CpuCriticalPercent),
            memory,
            Classify(memory?.UsedPercent, options.MemoryWarningPercent, options.MemoryCriticalPercent),
            disks,
            [.. history],
            OperatingSystem.IsWindows() ? null : WindowsOnlyNotice);
        return latest;
    }

    private double? ReadCpuUsagePercent()
    {
        var current = ReadProcessorTimes();
        var previous = previousProcessorTimes;
        previousProcessorTimes = current ?? previous;
        if (current is null || previous is null)
        {
            return null;
        }

        var idleDelta = current.Value.Idle - previous.Value.Idle;
        var totalDelta = current.Value.Kernel - previous.Value.Kernel + (current.Value.User - previous.Value.User);
        if (totalDelta <= 0 || idleDelta < 0)
        {
            return null;
        }

        // O tempo de kernel devolvido pelo Windows já inclui o tempo ocioso.
        var busy = totalDelta - idleDelta;
        return Math.Round(Math.Clamp(busy * 100d / totalDelta, 0, 100), 1);
    }

    private static ProcessorTimes? ReadProcessorTimes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return GetSystemTimes(out var idle, out var kernel, out var user)
                ? new ProcessorTimes(idle, kernel, user)
                : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static ServerMemoryUsage? ReadMemory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var buffer = new byte[MemoryStatusLength];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, MemoryStatusLength);
        try
        {
            if (!GlobalMemoryStatusEx(buffer))
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        var totalBytes = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(TotalPhysicalOffset));
        var availableBytes = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(AvailablePhysicalOffset));
        if (totalBytes == 0 || totalBytes > (ulong)long.MaxValue || availableBytes > totalBytes)
        {
            return null;
        }

        var total = (long)totalBytes;
        var available = (long)availableBytes;
        var used = total - available;
        return new ServerMemoryUsage(total, used, available, Math.Round(used * 100d / total, 1));
    }

    private List<ServerDiskUsage> ReadDisks()
    {
        var disks = new List<ServerDiskUsage>();
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return disks;
        }

        foreach (var drive in drives)
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady || drive.TotalSize <= 0)
                {
                    continue;
                }

                var total = drive.TotalSize;
                var free = Math.Clamp(drive.TotalFreeSpace, 0, total);
                var freePercent = Math.Round(free * 100d / total, 1);
                disks.Add(new ServerDiskUsage(
                    drive.Name,
                    ReadLabel(drive),
                    drive.DriveFormat,
                    total,
                    total - free,
                    free,
                    Math.Round(100 - freePercent, 1),
                    freePercent,
                    ClassifyFreeSpace(freePercent)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A unidade pode sumir ou negar acesso entre a enumeração e a leitura.
            }
        }

        return [.. disks.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static string? ReadLabel(DriveInfo drive)
    {
        try
        {
            return string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static HealthStatus Classify(double? usedPercent, double warningPercent, double criticalPercent)
    {
        if (usedPercent is null)
        {
            return HealthStatus.Unknown;
        }

        if (usedPercent >= criticalPercent)
        {
            return HealthStatus.Critical;
        }

        return usedPercent >= warningPercent ? HealthStatus.Warning : HealthStatus.Healthy;
    }

    private HealthStatus ClassifyFreeSpace(double freePercent)
    {
        if (freePercent <= options.DiskCriticalPercent)
        {
            return HealthStatus.Critical;
        }

        return freePercent <= options.DiskWarningPercent ? HealthStatus.Warning : HealthStatus.Healthy;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    /// <summary>
    /// MEMORYSTATUSEX é lido como bloco de bytes para não depender de um struct
    /// interoperável; os deslocamentos fixos são os documentados pelo Win32.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(byte[] buffer);

    private readonly record struct ProcessorTimes(long Idle, long Kernel, long User);
}
