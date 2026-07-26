using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.UnitTests;

public sealed class MaintenancePlannerTests
{
    private static readonly string[] ProductionServices = ["Producao-AppServer", "Producao-Broker"];
    private static readonly string[] ExclusiveServices = ["Compilacao-AppServer"];
    private static readonly string[] SingleProductionService = ["Producao-AppServer"];

    [Fact]
    public void ExclusiveInstallationRestartsWhileEverythingElseStops()
    {
        var plan = MaintenancePlanner.Create(
        [
            new MonitoredService("Producao-AppServer", false),
            new MonitoredService("Producao-Broker", false),
            new MonitoredService("Compilacao-AppServer", true)
        ]);

        Assert.Equal(ProductionServices, plan.ToStop);
        Assert.Equal(ExclusiveServices, plan.ToRestart);
    }

    [Fact]
    public void ServiceSharedWithExclusiveInstallationIsNeverStopped()
    {
        var plan = MaintenancePlanner.Create(
        [
            new MonitoredService("Compartilhado-DbAccess", false),
            new MonitoredService("compartilhado-dbaccess", true),
            new MonitoredService("Producao-AppServer", false)
        ]);

        Assert.Equal(SingleProductionService, plan.ToStop);
        Assert.Single(plan.ToRestart);
    }

    [Fact]
    public void WithoutExclusiveInstallationEverythingStops()
    {
        var plan = MaintenancePlanner.Create(
        [
            new MonitoredService("Producao-AppServer", false),
            new MonitoredService("Producao-AppServer", false)
        ]);

        Assert.Equal(SingleProductionService, plan.ToStop);
        Assert.Empty(plan.ToRestart);
    }
}

public sealed class AutoStartPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StoppedServiceWithoutPreviousAttemptIsRecovered()
    {
        Assert.True(AutoStartPolicy.ShouldAttempt("Stopped", null, Now));
    }

    [Fact]
    public void RunningServiceIsNeverRestarted()
    {
        Assert.False(AutoStartPolicy.ShouldAttempt("Running", null, Now));
        Assert.False(AutoStartPolicy.ShouldAttempt("StartPending", null, Now));
    }

    [Fact]
    public void ManualStopSuspendsRecoveryUntilAManualStart()
    {
        Assert.False(AutoStartPolicy.ShouldAttempt("Stopped", null, Now, manuallySuspended: true));
        Assert.True(AutoStartPolicy.ShouldAttempt("Stopped", null, Now, manuallySuspended: false));
    }

    [Fact]
    public void ServiceWithAnActionInFlightIsLeftAlone()
    {
        Assert.False(AutoStartPolicy.ShouldAttempt("Stopped", null, Now, actionInFlight: true));
    }

    [Fact]
    public void AttemptBudgetStopsTheLoopWithinTheWindow()
    {
        var attempt = new AutoStartAttempt(AutoStartPolicy.MaximumAttempts, Now.AddMinutes(-5));

        Assert.False(AutoStartPolicy.ShouldAttempt("Stopped", attempt, Now));
    }

    [Fact]
    public void AttemptBudgetResetsAfterTheWindow()
    {
        var attempt = new AutoStartAttempt(AutoStartPolicy.MaximumAttempts, Now - AutoStartPolicy.AttemptWindow.Add(TimeSpan.FromMinutes(1)));

        Assert.True(AutoStartPolicy.ShouldAttempt("Stopped", attempt, Now));
        Assert.Equal(1, AutoStartPolicy.Register(attempt, Now).Count);
    }

    [Fact]
    public void RegisterAccumulatesAttemptsInsideTheWindow()
    {
        var first = AutoStartPolicy.Register(null, Now);
        var second = AutoStartPolicy.Register(first, Now.AddMinutes(1));

        Assert.Equal(1, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal(first.FirstAttemptAt, second.FirstAttemptAt);
    }
}

public sealed class ServiceStateRulesTests
{
    [Theory]
    [InlineData("Running", "start", false)]
    [InlineData("Running", "stop", true)]
    [InlineData("Running", "restart", true)]
    [InlineData("Stopped", "start", true)]
    [InlineData("Stopped", "stop", false)]
    [InlineData("Stopped", "restart", false)]
    [InlineData("StartPending", "start", false)]
    [InlineData("StartPending", "stop", false)]
    [InlineData(null, "start", true)]
    [InlineData("NotFound", "stop", true)]
    public void ActionMatchingTheCurrentStateIsBlocked(string? status, string action, bool allowed)
    {
        Assert.Equal(allowed, ServiceStateRules.AllowsAction(status, action));
    }

    [Fact]
    public void UnknownActionIsRejected()
    {
        Assert.False(ServiceStateRules.AllowsAction("Running", "pause"));
    }
}
