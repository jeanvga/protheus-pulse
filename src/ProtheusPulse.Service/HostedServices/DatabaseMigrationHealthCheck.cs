using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ProtheusPulse.Service.HostedServices;

/// <summary>Prontidão do esquema: em migração ainda não é saudável; falha é crítica.</summary>
public sealed class DatabaseMigrationHealthCheck(DatabaseReadyState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (state.IsReady)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Banco migrado."));
        }

        return Task.FromResult(state.Failure is { Length: > 0 } failure
            ? HealthCheckResult.Unhealthy($"A migração do banco falhou: {failure}")
            : HealthCheckResult.Unhealthy("Migrando o banco local; o painel abre quando terminar."));
    }
}
