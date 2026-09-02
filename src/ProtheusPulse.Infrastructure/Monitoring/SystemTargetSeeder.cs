using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Infrastructure.Monitoring;

/// <summary>
/// Cria o alvo que representa a própria máquina. Processador, memória e disco do servidor
/// só chegam ao motor de alerta se houver um componente para pendurar probe e regra, e a
/// instalação que o hospeda fica marcada como de sistema para não poluir a aba Instalações.
/// </summary>
public sealed class SystemTargetSeeder(PulseDbContext dbContext, IClock clock) : ISystemTargetSeeder
{
    public const string InstallationName = "Servidor local";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.Installations.AnyAsync(item => item.IsSystem, cancellationToken))
        {
            return;
        }

        dbContext.Installations.Add(new Installation
        {
            Name = InstallationName,
            Environment = EnvironmentKind.Custom,
            CustomEnvironmentName = "Infraestrutura",
            IsSystem = true,
            CreatedAt = clock.UtcNow,
            Components =
            [
                new Component
                {
                    Name = Environment.MachineName,
                    Type = ComponentType.Generic,
                    IsRequired = true
                }
            ]
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
