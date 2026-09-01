using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Service.Observability;

public sealed class PulseTelemetry : IDisposable
{
    public const string MeterName = "ProtheusPulse.Service";
    private const int MaximumDimensionLength = 128;

    private readonly Meter meter;
    private readonly Histogram<double> probeDuration;
    private readonly Counter<long> collectionCycles;
    private readonly Histogram<double> collectionDuration;
    private readonly Histogram<long> collectionComponents;
    private readonly Counter<long> logEvents;
    private readonly ConcurrentDictionary<ProbeKey, ProbeState> probeStates = new();
    private readonly ConcurrentDictionary<ComponentKey, ComponentState> componentStates = new();

    public PulseTelemetry()
    {
        meter = new Meter(MeterName);
        probeDuration = meter.CreateHistogram<double>(
            "protheus.pulse.probe.duration",
            unit: "s",
            description: "Tempo gasto pelo Pulse para executar um probe.");
        meter.CreateObservableGauge(
            "protheus.pulse.probe.up",
            ObserveProbeStates,
            unit: "1",
            description: "Resultado mais recente do probe: 1 para saudável e 0 para os demais estados.");
        meter.CreateObservableGauge(
            "protheus.pulse.component.health",
            ObserveComponentStates,
            unit: "1",
            description: "Estado mais recente do componente: unknown=0, healthy=1, warning=2, critical=3, maintenance=4.");
        collectionCycles = meter.CreateCounter<long>(
            "protheus.pulse.collection.cycles",
            unit: "{cycle}",
            description: "Ciclos de coleta concluídos, separados por resultado.");
        collectionDuration = meter.CreateHistogram<double>(
            "protheus.pulse.collection.duration",
            unit: "s",
            description: "Duração total dos ciclos de coleta.");
        collectionComponents = meter.CreateHistogram<long>(
            "protheus.pulse.collection.components",
            unit: "{component}",
            description: "Quantidade de componentes processados por ciclo.");
        logEvents = meter.CreateCounter<long>(
            "protheus.pulse.log.events",
            unit: "{event}",
            description: "Eventos sanitizados encontrados pelo coletor incremental de logs.");
    }

    public void RecordProbe(
        string installation,
        string component,
        ProbeType probeType,
        HealthStatus status,
        bool required,
        TimeSpan duration)
    {
        var key = new ProbeKey(
            NormalizeDimension(installation),
            NormalizeDimension(component),
            probeType,
            required);
        var state = new ProbeState(status == HealthStatus.Healthy ? 1 : 0, status);
        probeStates[key] = state;

        var tags = CreateProbeTags(key, state.Status);
        probeDuration.Record(Math.Max(0, duration.TotalSeconds), in tags);
    }

    public void RecordComponentHealth(string installation, string component, HealthStatus status)
    {
        var key = new ComponentKey(NormalizeDimension(installation), NormalizeDimension(component));
        componentStates[key] = new ComponentState(ToHealthValue(status), status);
    }

    public void RecordCollectionCycle(bool success, int processedComponents, TimeSpan duration)
    {
        var outcome = success ? "success" : "failure";
        collectionCycles.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        collectionDuration.Record(
            Math.Max(0, duration.TotalSeconds),
            new KeyValuePair<string, object?>("outcome", outcome));
        collectionComponents.Record(
            Math.Max(0, processedComponents),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordLogEvent(string installation, string component, string level, int occurrenceCount)
    {
        if (occurrenceCount <= 0)
        {
            return;
        }

        var tags = new TagList
        {
            { "installation", NormalizeDimension(installation) },
            { "component", NormalizeDimension(component) },
            { "level", NormalizeLevel(level) }
        };
        logEvents.Add(occurrenceCount, in tags);
    }

    public void Dispose()
    {
        meter.Dispose();
        GC.SuppressFinalize(this);
    }

    private IEnumerable<Measurement<int>> ObserveProbeStates()
    {
        foreach (var (key, state) in probeStates)
        {
            var tags = CreateProbeTags(key, state.Status);
            yield return new Measurement<int>(state.Up, tags.ToArray());
        }
    }

    private IEnumerable<Measurement<int>> ObserveComponentStates()
    {
        foreach (var (key, state) in componentStates)
        {
            yield return new Measurement<int>(state.Value,
            [
                new("installation", key.Installation),
                new("component", key.Component),
                new("status", ToTagValue(state.Status)),
                new("maintenance", state.Status == HealthStatus.Maintenance)
            ]);
        }
    }

    private static TagList CreateProbeTags(ProbeKey key, HealthStatus status) => new()
    {
        { "installation", key.Installation },
        { "component", key.Component },
        { "probe.type", key.ProbeType.ToString().ToLowerInvariant() },
        { "required", key.Required },
        { "status", ToTagValue(status) }
    };

    private static int ToHealthValue(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => 1,
        HealthStatus.Warning => 2,
        HealthStatus.Critical => 3,
        HealthStatus.Maintenance => 4,
        _ => 0
    };

    private static string ToTagValue(HealthStatus status) => status.ToString().ToLowerInvariant();

    private static string NormalizeLevel(string value) => value.Trim().ToLowerInvariant() switch
    {
        "critical" => "critical",
        "error" => "error",
        "warning" or "warn" => "warning",
        "information" or "info" => "information",
        "debug" => "debug",
        "trace" => "trace",
        _ => "unknown"
    };

    private static string NormalizeDimension(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = new string(value
            .Take(MaximumDimensionLength)
            .Select(character => char.IsControl(character) ? '?' : character)
            .ToArray());
        return normalized.Trim();
    }

    private readonly record struct ProbeKey(
        string Installation,
        string Component,
        ProbeType ProbeType,
        bool Required);

    private readonly record struct ProbeState(int Up, HealthStatus Status);
    private readonly record struct ComponentKey(string Installation, string Component);
    private readonly record struct ComponentState(int Value, HealthStatus Status);
}
