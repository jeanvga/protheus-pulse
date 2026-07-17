using System.Diagnostics;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Infrastructure.Monitoring;

public sealed class HeartbeatProbeCollector(IClock clock) : IProbeCollector
{
    public ProbeType Type => ProbeType.Heartbeat;

    public bool CanCollect(Component component) => component.HeartbeatDefinitions.Count > 0;

    public Task<ProbeObservation> CollectAsync(Component component, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var now = clock.UtcNow;
        var localTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime);
        var observations = new List<CollectorSupport.TargetObservation>();
        var delays = new List<double>();

        foreach (var definition in component.HeartbeatDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsInsideWindow(definition, localTime))
            {
                observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Healthy, component.IsRequired));
                continue;
            }

            if (!definition.LastHeartbeatAt.HasValue)
            {
                observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Unknown, component.IsRequired));
                continue;
            }

            var delay = now - definition.LastHeartbeatAt.Value;
            var delayMinutes = Math.Max(0, delay.TotalMinutes);
            delays.Add(delayMinutes);
            var allowedDelay = TimeSpan.FromSeconds(definition.ExpectedIntervalSeconds + definition.ToleranceSeconds);
            observations.Add(new CollectorSupport.TargetObservation(
                delay <= allowedDelay ? HealthStatus.Healthy : HealthStatus.Critical,
                component.IsRequired));
        }

        IReadOnlyList<MetricObservation>? metrics = delays.Count == 0
            ? null
            : [new MetricObservation("heartbeatDelay", Math.Round(delays.Max(), 1), "min")];
        return Task.FromResult(CollectorSupport.CreateObservation(
            stopwatch,
            observations,
            now,
            "Todos os heartbeats estão dentro do intervalo esperado.",
            "Um heartbeat opcional está atrasado.",
            "Ao menos um heartbeat obrigatório está atrasado.",
            "Ainda não há heartbeat recebido para um job ativo.",
            metrics));
    }

    internal static bool IsInsideWindow(HeartbeatDefinition definition, TimeOnly localTime)
    {
        if (!definition.WindowStart.HasValue || !definition.WindowEnd.HasValue)
        {
            return true;
        }

        var start = definition.WindowStart.Value;
        var end = definition.WindowEnd.Value;
        return start < end
            ? localTime >= start && localTime < end
            : localTime >= start || localTime < end;
    }
}
