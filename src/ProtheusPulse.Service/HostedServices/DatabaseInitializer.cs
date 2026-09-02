using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Service.HostedServices;

// Como serviço Windows, o processo precisa registrar-se no SCM logo após o início do
// host. Migração e seed executados dentro de StartAsync rodam ANTES desse registro:
// num banco já grande a criação de índice passa da janela de 30 segundos e o start
// morre com o erro 1053. Por isso a migração roda em segundo plano e quem espera por
// ela é o health check de prontidão, não o Gerenciador de Serviços.
public sealed partial class DatabaseInitializer(
    IServiceProvider serviceProvider,
    bool seedDemoData,
    DatabaseReadyState readyState,
    ILogger<DatabaseInitializer> logger) : IHostedService, IDisposable
{
    private CancellationTokenSource? shutdown;
    private Task? migration;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        shutdown = new CancellationTokenSource();
        migration = Task.Run(() => MigrateAsync(shutdown.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (shutdown is not null)
            {
                await shutdown.CancelAsync();
            }
        }
        catch (ObjectDisposedException)
        {
            // Host já descartado: não há o que cancelar.
        }

        if (migration is not null)
        {
            // Espera curta e desatrelada do token de parada: a migração já foi cancelada,
            // e o encerramento não pode depender de um token que pode estar descartado.
            await Task.WhenAny(migration, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
        }
    }

    public void Dispose() => shutdown?.Dispose();

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        try
        {
            LogMigrationStarted(logger);
            await using var scope = serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<ISystemTargetSeeder>().SeedAsync(cancellationToken);
            if (seedDemoData)
            {
                var seeder = scope.ServiceProvider.GetRequiredService<IDemoDataSeeder>();
                await seeder.SeedAsync(cancellationToken);
            }

            readyState.MarkReady();
            LogDatabaseReady(logger);
        }
        catch (OperationCanceledException)
        {
            // Serviço parando durante a migração: nada a registrar.
        }
        catch (Exception exception)
        {
            // O processo segue de pé para que /health/ready informe a falha em vez de
            // deixar o instalador diante de um serviço que morreu sem explicação.
            readyState.MarkFailed(exception.Message);
            LogMigrationFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 1400, Level = LogLevel.Information, Message = "Migrando o banco local em segundo plano.")]
    private static partial void LogMigrationStarted(ILogger logger);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "Banco local migrado e pronto para uso.")]
    private static partial void LogDatabaseReady(ILogger logger);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Critical, Message = "A migração do banco local falhou; o painel fica indisponível até a causa ser corrigida.")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);
}
