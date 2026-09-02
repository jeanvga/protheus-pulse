using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProtheusPulse.Service.HostedServices;

namespace ProtheusPulse.UnitTests;

/// <summary>
/// A migração saiu do caminho de inicialização porque o SCM derruba o start em 30
/// segundos e um banco com meses de histórico passa disso. Quem informa a espera é o
/// health check de prontidão — se ele responder saudável cedo demais, o instalador dá a
/// instalação por concluída com o esquema pela metade.
/// </summary>
public sealed class DatabaseReadinessTests
{
    [Fact]
    public async Task ReadinessIsUnhealthyWhileTheSchemaIsStillMigrating()
    {
        var state = new DatabaseReadyState();

        var result = await new DatabaseMigrationHealthCheck(state).CheckHealthAsync(new HealthCheckContext());

        Assert.False(state.IsReady);
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Migrando", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadinessTurnsHealthyOnlyAfterTheSchemaIsApplied()
    {
        var state = new DatabaseReadyState();
        state.MarkReady();

        var result = await new DatabaseMigrationHealthCheck(state).CheckHealthAsync(new HealthCheckContext());

        Assert.True(state.IsReady);
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task AFailedMigrationIsReportedInsteadOfHiddenBehindADeadService()
    {
        var state = new DatabaseReadyState();
        state.MarkFailed("disco cheio");

        var result = await new DatabaseMigrationHealthCheck(state).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("disco cheio", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitingReleasesAsSoonAsTheSchemaIsReady()
    {
        var state = new DatabaseReadyState();
        var waiting = state.WaitAsync(CancellationToken.None);

        Assert.False(waiting.IsCompleted);
        state.MarkReady();

        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitingGivesUpWhenTheServiceIsStopping()
    {
        var state = new DatabaseReadyState();
        using var stopping = new CancellationTokenSource();
        var waiting = state.WaitAsync(stopping.Token);

        await stopping.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
    }
}
