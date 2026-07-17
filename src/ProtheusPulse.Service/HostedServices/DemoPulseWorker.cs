using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Hubs;

namespace ProtheusPulse.Service.HostedServices;

public sealed partial class DemoPulseWorker(
    IServiceScopeFactory scopeFactory,
    IHubContext<PulseHub> hubContext,
    IClock clock,
    ILogger<DemoPulseWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await AdvanceScenarioAsync(stoppingToken);
                await hubContext.Clients.All.SendAsync("dashboardUpdated", new { at = clock.UtcNow }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogScenarioFailure(logger, exception);
            }
        }
    }

    private async Task AdvanceScenarioAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var rule = await dbContext.AlertRules
            .Include(item => item.Component)
            .Include(item => item.Occurrences)
            .SingleAsync(item => item.RuleKey == "DEMO-TCP-INSTABILITY", cancellationToken);
        var active = rule.Occurrences.FirstOrDefault(item => item.State == AlertState.Active);
        var now = clock.UtcNow;

        if (active is not null && now - active.StartedAt >= TimeSpan.FromSeconds(45))
        {
            active.State = AlertState.Resolved;
            active.ResolvedAt = now;
            active.Evidence = "Conectividade restabelecida; incidente resolvido automaticamente (DEMO).";
            rule.Component.Status = HealthStatus.Healthy;
            rule.Component.LastStateChangeAt = now;
            var recoveredProbe = new ProbeResult
            {
                ComponentId = rule.ComponentId,
                ProbeType = ProbeType.Tcp,
                Status = HealthStatus.Healthy,
                ObservedAt = now,
                DurationMs = 17,
                Message = "Porta TCP recuperada; três verificações consecutivas bem-sucedidas.",
                EvidenceJson = "{\"demo\":true}",
                IsRequired = true
            };
            dbContext.ProbeResults.Add(recoveredProbe);
        }
        else if (active is null)
        {
            var last = rule.Occurrences.Max(item => (DateTimeOffset?)(item.ResolvedAt ?? item.StartedAt));
            if (last is null || now - last >= TimeSpan.FromSeconds(75))
            {
                var occurrence = new AlertOccurrence
                {
                    AlertRuleId = rule.Id,
                    State = AlertState.Active,
                    StartedAt = now,
                    Evidence = "Porta indisponível após três tentativas consecutivas (cenário DEMO)."
                };
                dbContext.AlertOccurrences.Add(occurrence);
                rule.Component.Status = HealthStatus.Critical;
                rule.Component.LastStateChangeAt = now;
                var failedProbe = new ProbeResult
                {
                    ComponentId = rule.ComponentId,
                    ProbeType = ProbeType.Tcp,
                    Status = HealthStatus.Critical,
                    ObservedAt = now,
                    DurationMs = 3_000,
                    Message = "Porta indisponível por três tentativas; incidente aberto.",
                    EvidenceJson = "{\"demo\":true}",
                    IsRequired = true
                };
                dbContext.ProbeResults.Add(failedProbe);
            }
        }

        var worker = await dbContext.Components.SingleAsync(item => item.IsDemo && item.Name == "Worker Financeiro", cancellationToken);
        dbContext.MetricSamples.Add(new MetricSample
        {
            ComponentId = worker.Id,
            Name = "memory",
            Value = Math.Round(86.5 + Math.Sin(now.Second / 8d) * 2.2, 1),
            Unit = "%",
            ObservedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Falha ao avançar o cenário de demonstração.")]
    private static partial void LogScenarioFailure(ILogger logger, Exception exception);
}
