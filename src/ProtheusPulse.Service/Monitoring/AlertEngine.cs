using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Service.Monitoring;

public sealed class AlertEngine(PulseDbContext dbContext, IClock clock)
{
    private static readonly HealthStatus[] DefaultTriggerStatuses = [HealthStatus.Warning, HealthStatus.Critical];

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

            var failure = IsFailure(rule, current.Observation.Status);
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

            if (occurrence is not null || !await HasMinimumFailuresAsync(component.Id, rule, current.Observation.Status, cancellationToken))
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
                Name = $"Falha no coletor {item.Type}",
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

    private async Task<bool> HasMinimumFailuresAsync(
        Guid componentId,
        AlertRule rule,
        HealthStatus currentStatus,
        CancellationToken cancellationToken)
    {
        var minimum = Math.Clamp(rule.MinimumConsecutiveFailures, 1, 20);
        if (minimum == 1)
        {
            return true;
        }

        var previous = await dbContext.ProbeResults
            .AsNoTracking()
            .Where(item => item.ComponentId == componentId && item.ProbeType == rule.ProbeType)
            .OrderByDescending(item => item.ObservedAt)
            .Take(minimum - 1)
            .Select(item => item.Status)
            .ToListAsync(cancellationToken);
        return previous.Count == minimum - 1
            && IsFailure(rule, currentStatus)
            && previous.All(status => IsFailure(rule, status));
    }

    private static bool IsFailure(AlertRule rule, HealthStatus status)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<AlertRuleConfiguration>(rule.ConfigurationJson);
            var configured = configuration?.TriggerStatuses?
                .Select(value => Enum.TryParse<HealthStatus>(value, ignoreCase: true, out var parsed) ? parsed : (HealthStatus?)null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            return (configured is { Length: > 0 } ? configured : DefaultTriggerStatuses).Contains(status);
        }
        catch (JsonException)
        {
            return DefaultTriggerStatuses.Contains(status);
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

    private sealed record AlertRuleConfiguration(IReadOnlyList<string>? TriggerStatuses);
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
