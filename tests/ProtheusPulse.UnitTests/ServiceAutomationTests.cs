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
    public void StoppedServiceWithoutPreviousFailureIsRecovered()
    {
        Assert.True(AutoStartPolicy.ShouldAttempt("Stopped", AutoStartState.Clean, Now));
    }

    [Fact]
    public void RunningServiceIsNeverRestarted()
    {
        Assert.False(AutoStartPolicy.ShouldAttempt("Running", AutoStartState.Clean, Now));
        Assert.False(AutoStartPolicy.ShouldAttempt("StartPending", AutoStartState.Clean, Now));
    }

    [Fact]
    public void ManualStopSuspendsRecoveryUntilAManualStart()
    {
        var suspended = AutoStartState.Clean with { Suspended = true };

        Assert.False(AutoStartPolicy.ShouldAttempt("Stopped", suspended, Now));
        Assert.True(AutoStartPolicy.ShouldAttempt("Stopped", AutoStartState.Clean, Now));
    }

    [Fact]
    public void ServiceWithAnActionInFlightIsLeftAlone()
    {
        Assert.False(AutoStartPolicy.ShouldAttempt("Stopped", AutoStartState.Clean, Now, actionInFlight: true));
    }

    [Fact]
    public void BackoffHoldsTheNextAttemptUntilTheRetryMoment()
    {
        var failed = AutoStartPolicy.RegisterFailure(AutoStartState.Clean, Now);

        Assert.Equal(Now + AutoStartPolicy.FirstRetryDelay, failed.RetryAfter);
        Assert.False(AutoStartPolicy.ShouldAttempt("Stopped", failed, Now.AddSeconds(30)));
        Assert.True(AutoStartPolicy.ShouldAttempt("Stopped", failed, Now + AutoStartPolicy.FirstRetryDelay));
    }

    [Fact]
    public void EachFailureDoublesTheWaitUpToTheCap()
    {
        Assert.Equal(AutoStartPolicy.FirstRetryDelay, AutoStartPolicy.BackoffFor(1));
        Assert.Equal(TimeSpan.FromMinutes(2), AutoStartPolicy.BackoffFor(2));
        Assert.Equal(TimeSpan.FromMinutes(4), AutoStartPolicy.BackoffFor(3));
        Assert.Equal(AutoStartPolicy.MaximumRetryDelay, AutoStartPolicy.BackoffFor(20));
    }

    [Fact]
    public void WatchdogGivesUpAfterTheFailureBudgetAndStaysQuiet()
    {
        var state = AutoStartState.Clean;
        for (var attempt = 0; attempt < AutoStartPolicy.MaximumFailures; attempt++)
        {
            state = AutoStartPolicy.RegisterFailure(state, Now.AddHours(attempt));
        }

        Assert.True(state.Suspended);
        Assert.Equal(AutoStartPolicy.MaximumFailures, state.FailureCount);
        Assert.False(AutoStartPolicy.ShouldAttempt("Stopped", state, Now.AddDays(30)));
    }

    [Fact]
    public void SuccessClearsTheFailureBudget()
    {
        var recovered = AutoStartPolicy.RegisterSuccess();

        Assert.Equal(0, recovered.FailureCount);
        Assert.Null(recovered.RetryAfter);
        Assert.False(recovered.Suspended);
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
