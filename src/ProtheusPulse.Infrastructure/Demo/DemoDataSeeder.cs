using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Infrastructure.Demo;

public sealed class DemoDataSeeder(PulseDbContext dbContext, IClock clock, IPasswordService passwordService) : IDemoDataSeeder
{
    public const string DemoUsername = "demo.admin";
    public const string DemoPassword = "PulseDemo!2026";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.Installations.AnyAsync(item => item.IsDemo, cancellationToken))
        {
            return;
        }

        var now = clock.UtcNow;
        var production = new Installation
        {
            Name = "ERP Produção · DEMO",
            Environment = EnvironmentKind.Production,
            IsDemo = true,
            TagsJson = "[\"Matriz\",\"Servidor A\"]",
            CreatedAt = now.AddDays(-21)
        };
        var homologation = new Installation
        {
            Name = "Integrações Homologação · DEMO",
            Environment = EnvironmentKind.Homologation,
            IsDemo = true,
            TagsJson = "[\"Integrações\",\"Servidor B\"]",
            CreatedAt = now.AddDays(-10)
        };

        var rest = CreateComponent(production, "AppServer REST", ComponentType.Rest, HealthStatus.Healthy, now.AddHours(-37));
        var worker = CreateComponent(production, "Worker Financeiro", ComponentType.Worker, HealthStatus.Warning, now.AddMinutes(-43));
        var job = CreateComponent(production, "Job Fechamento", ComponentType.Job, HealthStatus.Critical, now.AddMinutes(-18));
        var portal = CreateComponent(homologation, "Portal HTTPS", ComponentType.HttpEndpoint, HealthStatus.Warning, now.AddHours(-2));
        var broker = CreateComponent(homologation, "Broker de Integrações", ComponentType.Broker, HealthStatus.Healthy, now.AddDays(-3));
        var console = CreateComponent(homologation, "Console de Integração", ComponentType.Generic, HealthStatus.Warning, now.AddMinutes(-26));

        AddProbe(rest, ProbeType.Http, HealthStatus.Healthy, now, 84, "HTTP 200 em 84 ms; TCP e serviço disponíveis.");
        AddProbe(worker, ProbeType.Process, HealthStatus.Warning, now, 31, "Memória acima do limite por 12 minutos; processo responsivo.");
        AddProbe(job, ProbeType.Heartbeat, HealthStatus.Critical, now, 4, "Heartbeat esperado há 18 minutos; tolerância de 5 minutos excedida.");
        AddProbe(portal, ProbeType.TlsCertificate, HealthStatus.Warning, now, 112, "Certificado válido, com vencimento em 9 dias.");
        AddProbe(broker, ProbeType.Tcp, HealthStatus.Healthy, now, 16, "Porta TCP disponível; latência dentro do esperado.");
        AddProbe(console, ProbeType.Log, HealthStatus.Warning, now, 9, "8 erros semelhantes agrupados nos últimos 15 minutos.");

        AddMetric(rest, "latency", 84, "ms", now);
        AddMetric(worker, "memory", 87, "%", now);
        AddMetric(job, "heartbeatDelay", 18, "min", now);
        AddMetric(portal, "certificateDays", 9, "dias", now);
        AddMetric(broker, "latency", 16, "ms", now);
        AddMetric(console, "errors", 8, "eventos", now);

        for (var hour = 0; hour < 12; hour++)
        {
            foreach (var component in production.Components.Concat(homologation.Components))
            {
                var baseValue = component.Status switch
                {
                    HealthStatus.Healthy => 100,
                    HealthStatus.Warning => 98.4,
                    HealthStatus.Critical => 93.5,
                    _ => 96
                };
                AddMetric(component, "availability", Math.Clamp(baseValue + Math.Sin(hour + component.Name.Length) * 0.7, 0, 100), "%", now.AddHours(hour - 11));
            }
        }

        var jobRule = CreateRule(job, "Heartbeat atrasado", "DEMO-HEARTBEAT-LATE", ProbeType.Heartbeat, AlertSeverity.Critical);
        jobRule.Occurrences.Add(new AlertOccurrence
        {
            State = AlertState.Active,
            StartedAt = now.AddMinutes(-18),
            Evidence = "Último sinal recebido fora da janela esperada (conteúdo sanitizado)."
        });

        var certificateRule = CreateRule(portal, "Certificado próximo do vencimento", "DEMO-TLS-EXPIRING", ProbeType.TlsCertificate, AlertSeverity.Warning);
        certificateRule.Occurrences.Add(new AlertOccurrence
        {
            State = AlertState.Active,
            StartedAt = now.AddHours(-2),
            Evidence = "Restam 9 dias de validade; limite configurado: 30 dias."
        });

        var memoryRule = CreateRule(worker, "Memória sustentada acima de 85%", "DEMO-MEMORY-HIGH", ProbeType.Process, AlertSeverity.Warning);
        memoryRule.Occurrences.Add(new AlertOccurrence
        {
            State = AlertState.Acknowledged,
            StartedAt = now.AddMinutes(-43),
            AcknowledgedAt = now.AddMinutes(-31),
            Evidence = "Uso médio de memória em 87% durante a janela de 10 minutos."
        });

        var incidentRule = CreateRule(broker, "Instabilidade de porta", "DEMO-TCP-INSTABILITY", ProbeType.Tcp, AlertSeverity.Critical);
        incidentRule.Occurrences.Add(new AlertOccurrence
        {
            State = AlertState.Resolved,
            StartedAt = now.AddHours(-5),
            ResolvedAt = now.AddHours(-4).AddMinutes(-46),
            Evidence = "Porta indisponível por 3 tentativas; recuperação detectada automaticamente."
        });

        dbContext.Installations.AddRange(production, homologation);
        dbContext.NotificationChannels.Add(new NotificationChannel
        {
            Name = "Dashboard local",
            Type = NotificationChannelType.Dashboard,
            Enabled = true
        });

        if (!await dbContext.Users.AnyAsync(cancellationToken))
        {
            dbContext.Users.Add(new User
            {
                Username = DemoUsername,
                DisplayName = "Administrador da demonstração",
                PasswordHash = passwordService.Hash(DemoPassword),
                Role = UserRole.Administrator,
                IsActive = true,
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Component CreateComponent(Installation installation, string name, ComponentType type, HealthStatus status, DateTimeOffset changedAt)
    {
        var component = new Component
        {
            Name = name,
            Type = type,
            Status = status,
            IsRequired = true,
            IsDemo = true,
            LastStateChangeAt = changedAt
        };
        installation.Components.Add(component);
        return component;
    }

    private static void AddProbe(Component component, ProbeType type, HealthStatus status, DateTimeOffset at, long duration, string message)
    {
        component.ProbeResults.Add(new ProbeResult
        {
            ProbeType = type,
            Status = status,
            ObservedAt = at,
            DurationMs = duration,
            Message = message,
            EvidenceJson = "{\"demo\":true}",
            IsRequired = true
        });
    }

    private static void AddMetric(Component component, string name, double value, string unit, DateTimeOffset at)
    {
        component.MetricSamples.Add(new MetricSample { Name = name, Value = value, Unit = unit, ObservedAt = at });
    }

    private static AlertRule CreateRule(Component component, string name, string key, ProbeType type, AlertSeverity severity)
    {
        var rule = new AlertRule
        {
            Name = name,
            RuleKey = key,
            ProbeType = type,
            Severity = severity,
            Enabled = true,
            MinimumConsecutiveFailures = 2,
            CooldownSeconds = 300
        };
        component.AlertRules.Add(rule);
        return rule;
    }
}
