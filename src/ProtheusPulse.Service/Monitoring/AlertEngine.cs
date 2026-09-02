using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Service.Monitoring;

public sealed class AlertEngine(PulseDbContext dbContext, IClock clock)
{
    private static readonly HealthStatus[] DefaultTriggerStatuses = [HealthStatus.Warning, HealthStatus.Critical];

    /// <summary>
    /// A configuração é gravada em camelCase e o record que a lê é PascalCase: sem ignorar a caixa,
    /// toda regra caía no padrão e os estados escolhidos na tela não valiam nada.
    /// </summary>
    private static readonly JsonSerializerOptions ConfigurationSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<AlertTransition>> EvaluateAsync(
        Component component,
        IReadOnlyList<(ProbeType Type, ProbeObservation Observation)> observations,
        bool maintenanceActive,
        CancellationToken cancellationToken)
    {
        var defaultRules = await EnsureDefaultRulesAsync(component, observations, cancellationToken);
        var rules = await dbContext.AlertRules
            .Where(item => item.ComponentId == component.Id && item.Enabled)
            .ToListAsync(cancellationToken);
        rules.AddRange(defaultRules);
        var transitions = new List<AlertTransition>();
        foreach (var rule in rules)
        {
            var current = observations.LastOrDefault(item => item.Type == rule.ProbeType);
            if (current.Observation is null)
            {
                continue;
            }

            var occurrence = await dbContext.AlertOccurrences
                .Where(item => item.AlertRuleId == rule.Id && item.State != AlertState.Resolved)
                .OrderByDescending(item => item.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (maintenanceActive)
            {
                if (occurrence is not null && occurrence.State != AlertState.Silenced)
                {
                    occurrence.State = AlertState.Silenced;
                }

                continue;
            }

            var configuration = ReadConfiguration(rule.ConfigurationJson);
            var failure = IsFailure(rule.ProbeType, configuration, current.Observation);
            if (occurrence?.State == AlertState.Silenced)
            {
                if (failure)
                {
                    occurrence.State = AlertState.Active;
                    transitions.Add(CreateTransition(rule, occurrence, AlertTransitionKind.Reactivated));
                }
                else
                {
                    Resolve(occurrence, current.Observation.Message);
                    transitions.Add(CreateTransition(rule, occurrence, AlertTransitionKind.Resolved));
                }

                continue;
            }

            if (!failure)
            {
                if (occurrence is not null)
                {
                    Resolve(occurrence, current.Observation.Message);
                    transitions.Add(CreateTransition(rule, occurrence, AlertTransitionKind.Resolved));
                }

                continue;
            }

            if (occurrence is not null || !await HasMinimumFailuresAsync(component.Id, rule, configuration, cancellationToken))
            {
                continue;
            }

            var lastResolution = await dbContext.AlertOccurrences
                .Where(item => item.AlertRuleId == rule.Id && item.ResolvedAt != null)
                .MaxAsync(item => item.ResolvedAt, cancellationToken);
            if (lastResolution.HasValue && clock.UtcNow - lastResolution.Value < TimeSpan.FromSeconds(rule.CooldownSeconds))
            {
                continue;
            }

            var created = new AlertOccurrence
            {
                AlertRuleId = rule.Id,
                State = AlertState.Active,
                StartedAt = clock.UtcNow,
                Evidence = Bound(current.Observation.Message, 2_000)
            };
            dbContext.AlertOccurrences.Add(created);
            transitions.Add(CreateTransition(rule, created, AlertTransitionKind.Opened));
        }

        return transitions;
    }

    private async Task<IReadOnlyList<AlertRule>> EnsureDefaultRulesAsync(
        Component component,
        IReadOnlyList<(ProbeType Type, ProbeObservation Observation)> observations,
        CancellationToken cancellationToken)
    {
        var existingTypes = await dbContext.AlertRules
            .Where(item => item.ComponentId == component.Id)
            .Select(item => item.ProbeType)
            .ToListAsync(cancellationToken);
        var created = new List<AlertRule>();
        foreach (var item in observations.Where(item => !existingTypes.Contains(item.Type)))
        {
            var rule = new AlertRule
            {
                ComponentId = component.Id,
                Name = DefaultRuleName(item.Type),
                RuleKey = $"AUTO-{component.Id:N}-{item.Type}",
                ProbeType = item.Type,
                Severity = item.Observation.IsRequired ? AlertSeverity.Critical : AlertSeverity.Warning,
                MinimumConsecutiveFailures = 2,
                CooldownSeconds = 300,
                ConfigurationJson = "{\"triggerStatuses\":[\"Warning\",\"Critical\"]}"
            };
            created.Add(rule);
            dbContext.AlertRules.Add(rule);
        }

        return created;
    }

    /// <summary>
    /// O alerta só abre depois de o mesmo problema aparecer em coletas seguidas. A contagem
    /// olha o histórico já gravado; a coleta em curso ainda não foi salva e entra como a última.
    /// </summary>
    private async Task<bool> HasMinimumFailuresAsync(
        Guid componentId,
        AlertRule rule,
        RuleConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var minimum = Math.Clamp(rule.MinimumConsecutiveFailures, 1, 20);
        if (minimum == 1)
        {
            return true;
        }

        if (configuration.ThresholdPercent is { } threshold && ServerMetricNames.ForProbe(rule.ProbeType) is { } metricName)
        {
            // O estado gravado no histórico foi classificado pelo limite global do servidor,
            // não pelo desta regra: para um limiar próprio só a própria medida serve.
            var samples = await dbContext.MetricSamples
                .AsNoTracking()
                .Where(item => item.ComponentId == componentId && item.Name == metricName)
                .OrderByDescending(item => item.ObservedAt)
                .Take(minimum - 1)
                .Select(item => item.Value)
                .ToListAsync(cancellationToken);
            return samples.Count == minimum - 1 && samples.All(value => value > threshold);
        }

        var previous = await dbContext.ProbeResults
            .AsNoTracking()
            .Where(item => item.ComponentId == componentId && item.ProbeType == rule.ProbeType)
            .OrderByDescending(item => item.ObservedAt)
            .Take(minimum - 1)
            .Select(item => item.Status)
            .ToListAsync(cancellationToken);
        return previous.Count == minimum - 1 && previous.All(configuration.TriggerStatuses.Contains);
    }

    /// <summary>
    /// Com limiar próprio a regra compara a medida; sem ele, compara o estado que o coletor
    /// classificou pelos limites globais da aba Servidor.
    /// </summary>
    private static bool IsFailure(ProbeType probeType, RuleConfiguration configuration, ProbeObservation observation)
    {
        if (configuration.ThresholdPercent is { } threshold && ServerMetricNames.ForProbe(probeType) is { } metricName)
        {
            var usage = observation.Metrics?.FirstOrDefault(item => item.Name == metricName)?.Value;
            if (usage.HasValue)
            {
                return usage.Value > threshold;
            }
        }

        return configuration.TriggerStatuses.Contains(observation.Status);
    }

    private static string DefaultRuleName(ProbeType probeType) => probeType switch
    {
        ProbeType.ServerCpu => "Processador do servidor",
        ProbeType.ServerMemory => "Memória do servidor",
        ProbeType.ServerDisk => "Discos do servidor",
        _ => $"Falha no coletor {probeType}"
    };

    /// <summary>Estados que a regra trata como falha; cai no padrão quando a configuração está vazia ou ilegível.</summary>
    public static IReadOnlyList<HealthStatus> ReadTriggerStatuses(string configurationJson) =>
        ReadConfiguration(configurationJson).TriggerStatuses;

    /// <summary>Limiar de uso em percentual da regra, quando ela define um próprio.</summary>
    public static double? ReadThresholdPercent(string configurationJson) =>
        ReadConfiguration(configurationJson).ThresholdPercent;

    private static RuleConfiguration ReadConfiguration(string configurationJson)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<AlertRuleConfiguration>(configurationJson, ConfigurationSerializerOptions);
            var configured = configuration?.TriggerStatuses?
                .Select(value => Enum.TryParse<HealthStatus>(value, ignoreCase: true, out var parsed) ? parsed : (HealthStatus?)null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            var threshold = configuration?.ThresholdPercent is { } value and > 0 and <= 100 ? value : (double?)null;
            return new RuleConfiguration(configured is { Length: > 0 } ? configured : DefaultTriggerStatuses, threshold);
        }
        catch (JsonException)
        {
            return new RuleConfiguration(DefaultTriggerStatuses, null);
        }
    }

    private void Resolve(AlertOccurrence occurrence, string evidence)
    {
        occurrence.State = AlertState.Resolved;
        occurrence.ResolvedAt = clock.UtcNow;
        occurrence.Evidence = Bound(evidence, 2_000);
    }

    private static AlertTransition CreateTransition(
        AlertRule rule,
        AlertOccurrence occurrence,
        AlertTransitionKind kind) =>
        new(
            occurrence.Id,
            occurrence.CorrelationId,
            rule.Severity,
            occurrence.State,
            kind,
            rule.Name);

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record AlertRuleConfiguration(IReadOnlyList<string>? TriggerStatuses, double? ThresholdPercent);

    private sealed record RuleConfiguration(IReadOnlyList<HealthStatus> TriggerStatuses, double? ThresholdPercent);
}

public sealed record AlertTransition(
    Guid OccurrenceId,
    Guid CorrelationId,
    AlertSeverity Severity,
    AlertState State,
    AlertTransitionKind Kind,
    string RuleName);

public enum AlertTransitionKind
{
    Opened,
    Resolved,
    Reactivated
}
