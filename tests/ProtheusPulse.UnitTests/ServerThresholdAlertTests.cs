using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Monitoring;

namespace ProtheusPulse.UnitTests;

/// <summary>
/// O limite de uso da regra existe justamente para não depender dos limites globais que
/// pintam a aba Servidor: quem quer ser avisado com 90% de memória não pode ficar preso
/// aos 85% de atenção do appsettings. Estes testes fixam essa independência.
/// </summary>
public sealed class ServerThresholdAlertTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly PulseDbContext dbContext;
    private readonly FixedClock clock = new(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));

    public ServerThresholdAlertTests()
    {
        connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        dbContext = new PulseDbContext(new DbContextOptionsBuilder<PulseDbContext>().UseSqlite(connection).Options);
        dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        dbContext.Dispose();
        connection.Dispose();
    }

    [Theory]
    [InlineData(93.4, true)]
    [InlineData(90.1, true)]
    [InlineData(90, false)]
    [InlineData(87.2, false)]
    public async Task OLimiteDaRegraDecideMesmoComOColetorRelatandoSaudavel(double usagePercent, bool shouldOpen)
    {
        var component = await SeedServerComponentAsync(thresholdPercent: 90);

        var transitions = await EvaluateAsync(component, HealthStatus.Healthy, usagePercent);

        Assert.Equal(shouldOpen, transitions.Count == 1);
        if (shouldOpen)
        {
            Assert.Equal(AlertTransitionKind.Opened, transitions[0].Kind);
            Assert.Equal(AlertSeverity.Warning, transitions[0].Severity);
        }
    }

    [Fact]
    public async Task SemLimiteProprioARegraVoltaASeguirOEstadoDoColetor()
    {
        var component = await SeedServerComponentAsync(thresholdPercent: null);

        var below = await EvaluateAsync(component, HealthStatus.Healthy, 99);
        var above = await EvaluateAsync(component, HealthStatus.Critical, 1);

        Assert.Empty(below);
        Assert.Single(above);
    }

    [Fact]
    public async Task UmaLeituraIndisponivelNaoInventaFalhaPeloLimite()
    {
        var component = await SeedServerComponentAsync(thresholdPercent: 90);

        // Processador e memória dependem de API do Windows: fora dele a leitura não existe.
        var transitions = await EvaluateAsync(component, HealthStatus.Unknown, usagePercent: null);

        Assert.Empty(transitions);
    }

    private async Task<Component> SeedServerComponentAsync(double? thresholdPercent)
    {
        var configuration = thresholdPercent is null
            ? "{\"triggerStatuses\":[\"Warning\",\"Critical\"]}"
            : $"{{\"triggerStatuses\":[\"Warning\",\"Critical\"],\"thresholdPercent\":{thresholdPercent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
        var component = new Component { Name = "SRV-PULSE", Type = ComponentType.Generic };
        dbContext.Installations.Add(new Installation
        {
            Name = "Servidor local",
            Environment = EnvironmentKind.Custom,
            IsSystem = true,
            CreatedAt = clock.UtcNow,
            Components = [component]
        });
        dbContext.AlertRules.Add(new AlertRule
        {
            Component = component,
            Name = "Memória acima de 90%",
            RuleKey = $"CUSTOM-{Guid.NewGuid():N}",
            ProbeType = ProbeType.ServerMemory,
            Severity = AlertSeverity.Warning,
            MinimumConsecutiveFailures = 1,
            CooldownSeconds = 0,
            ConfigurationJson = configuration
        });
        await dbContext.SaveChangesAsync();
        return component;
    }

    private async Task<IReadOnlyList<AlertTransition>> EvaluateAsync(Component component, HealthStatus status, double? usagePercent)
    {
        var observation = new ProbeObservation(
            status,
            clock.UtcNow,
            TimeSpan.FromMilliseconds(3),
            "Leitura sintética.",
            null,
            true,
            usagePercent is { } usage ? [new MetricObservation(ServerMetricNames.Memory, usage, "%")] : null);
        var transitions = await new AlertEngine(dbContext, clock).EvaluateAsync(
            component,
            [(ProbeType.ServerMemory, observation)],
            maintenanceActive: false,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        return transitions;
    }

    private sealed class FixedClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);
    }
}
