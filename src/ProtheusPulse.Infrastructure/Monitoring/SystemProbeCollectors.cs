using System.Diagnostics;
using System.ServiceProcess;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace ProtheusPulse.Infrastructure.Monitoring;

public sealed class WindowsServiceProbeCollector(IClock clock) : IProbeCollector
{
    public ProbeType Type => ProbeType.WindowsService;

    public bool CanCollect(Component component) => component.WindowsServiceTargets.Count > 0;

    public Task<ProbeObservation> CollectAsync(Component component, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observations = new List<CollectorSupport.TargetObservation>();
        if (!OperatingSystem.IsWindows())
        {
            observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Unknown, component.IsRequired));
            return Task.FromResult(CollectorSupport.CreateObservation(
                stopwatch, observations, clock.UtcNow,
                "Serviços em execução.", "Serviços exigem atenção.", "Serviço obrigatório parado.",
                "Coleta de serviço Windows indisponível nesta plataforma."));
        }

        var statuses = new Dictionary<string, ServiceControllerStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in ServiceController.GetServices())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (service)
            {
                try
                {
                    statuses[service.ServiceName] = service.Status;
                }
                catch (InvalidOperationException)
                {
                    // O serviço pode desaparecer entre a enumeração e a leitura.
                }
            }
        }

        foreach (var target in component.WindowsServiceTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = statuses.TryGetValue(target.ServiceName, out var serviceStatus)
                ? serviceStatus switch
                {
                    ServiceControllerStatus.Running => HealthStatus.Healthy,
                    ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending => HealthStatus.Warning,
                    _ => HealthStatus.Critical
                }
                : HealthStatus.Critical;
            observations.Add(new CollectorSupport.TargetObservation(status, component.IsRequired));
        }

        return Task.FromResult(CollectorSupport.CreateObservation(
            stopwatch, observations, clock.UtcNow,
            "Todos os serviços configurados estão em execução.",
            "Um serviço configurado está em transição ou é opcional e está parado.",
            "Ao menos um serviço obrigatório não está em execução.",
            "Não foi possível determinar o estado dos serviços."));
    }
}

public sealed class ProcessProbeCollector(IClock clock) : IProbeCollector
{
    public ProbeType Type => ProbeType.Process;

    public bool CanCollect(Component component) =>
        component.ProcessTargets.Any(item => !string.IsNullOrWhiteSpace(item.ExecutablePath));

    public Task<ProbeObservation> CollectAsync(Component component, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observations = new List<CollectorSupport.TargetObservation>();
        if (!OperatingSystem.IsWindows())
        {
            observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Unknown, component.IsRequired));
            return Task.FromResult(CollectorSupport.CreateObservation(
                stopwatch, observations, clock.UtcNow,
                "Processos em execução.", "Processos exigem atenção.", "Processo obrigatório ausente.",
                "Coleta de processos Windows indisponível nesta plataforma."));
        }

        foreach (var target in component.ProcessTargets.Where(item => !string.IsNullOrWhiteSpace(item.ExecutablePath)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            observations.Add(new CollectorSupport.TargetObservation(
                FindProcess(target.ExecutablePath!, cancellationToken),
                component.IsRequired));
        }

        return Task.FromResult(CollectorSupport.CreateObservation(
            stopwatch, observations, clock.UtcNow,
            "Todos os processos configurados estão em execução.",
            "Não foi possível confirmar todos os processos configurados.",
            "Ao menos um processo obrigatório não está em execução.",
            "Não foi possível determinar o estado dos processos."));
    }

    private static HealthStatus FindProcess(string executablePath, CancellationToken cancellationToken)
    {
        string expectedPath;
        try
        {
            expectedPath = Path.GetFullPath(executablePath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return HealthStatus.Unknown;
        }

        var processName = Path.GetFileNameWithoutExtension(expectedPath);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return HealthStatus.Unknown;
        }

        var inaccessible = false;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var actualPath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(actualPath)
                        && string.Equals(Path.GetFullPath(actualPath), expectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return HealthStatus.Healthy;
                    }
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    inaccessible = true;
                }
            }
        }

        return inaccessible ? HealthStatus.Unknown : HealthStatus.Critical;
    }
}

public sealed class FileProbeCollector(IClock clock) : IProbeCollector
{
    public ProbeType Type => ProbeType.File;

    public bool CanCollect(Component component) => component.FileTargets.Count > 0;

    public Task<ProbeObservation> CollectAsync(Component component, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observations = new List<CollectorSupport.TargetObservation>();
        foreach (var target in component.FileTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var exists = target.Kind == FileTargetKind.Directory
                    ? Directory.Exists(target.Path)
                    : File.Exists(target.Path);
                var status = !exists
                    ? HealthStatus.Critical
                    : (File.GetAttributes(target.Path) & FileAttributes.ReparsePoint) != 0
                        ? HealthStatus.Warning
                        : HealthStatus.Healthy;
                observations.Add(new CollectorSupport.TargetObservation(status, target.IsRequired));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Unknown, target.IsRequired));
            }
        }

        return Task.FromResult(CollectorSupport.CreateObservation(
            stopwatch, observations, clock.UtcNow,
            "Todos os arquivos e diretórios configurados estão acessíveis.",
            "Um caminho configurado é opcional, indireto ou exige atenção.",
            "Ao menos um arquivo ou diretório obrigatório não está acessível.",
            "Não foi possível consultar todos os caminhos configurados."));
    }
}

public sealed class DiskProbeCollector(IClock clock, ProbeCollectorOptions options) : IProbeCollector
{
    public ProbeType Type => ProbeType.Disk;

    public bool CanCollect(Component component) =>
        component.FileTargets.Count > 0 || component.ProcessTargets.Count > 0 || component.LogSources.Count > 0;

    public Task<ProbeObservation> CollectAsync(Component component, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var paths = component.FileTargets.Select(item => item.Path)
            .Concat(component.ProcessTargets.Select(item => item.ExecutablePath).Where(item => item is not null)!)
            .Concat(component.LogSources.Select(item => item.Path));
        var roots = paths.Select(Path.GetPathRoot)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
        var observations = new List<CollectorSupport.TargetObservation>();
        var freePercentages = new List<double>();
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var drive = new DriveInfo(root!);
                if (!drive.IsReady || drive.TotalSize <= 0)
                {
                    observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Unknown, component.IsRequired));
                    continue;
                }

                var freePercent = drive.AvailableFreeSpace * 100d / drive.TotalSize;
                freePercentages.Add(freePercent);
                var status = freePercent <= options.DiskCriticalPercent
                    ? HealthStatus.Critical
                    : freePercent <= options.DiskWarningPercent
                        ? HealthStatus.Warning
                        : HealthStatus.Healthy;
                observations.Add(new CollectorSupport.TargetObservation(status, component.IsRequired));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Unknown, component.IsRequired));
            }
        }

        if (observations.Count == 0)
        {
            observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Unknown, component.IsRequired));
        }

        IReadOnlyList<MetricObservation>? metrics = freePercentages.Count == 0
            ? null
            : [new MetricObservation("diskFree", Math.Round(freePercentages.Min(), 1), "%")];
        return Task.FromResult(CollectorSupport.CreateObservation(
            stopwatch, observations, clock.UtcNow,
            "Espaço livre em disco dentro dos limites.",
            "Espaço livre em disco abaixo do limite de atenção.",
            "Espaço livre em disco abaixo do limite crítico.",
            "Não foi possível determinar o espaço livre em disco.",
            metrics));
    }
}
