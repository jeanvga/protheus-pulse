using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Service.HostedServices;

// Como serviço Windows, o processo precisa registrar-se no SCM logo após o
// início do host; migração e seed executados antes de RunAsync estouram a
// janela de 30 segundos do SCM e derrubam o start com o erro 1053.
public sealed partial class DatabaseInitializer(
    IServiceProvider serviceProvider,
    bool seedDemoData,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
        if (seedDemoData)
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IDemoDataSeeder>();
            await seeder.SeedAsync(cancellationToken);
        }

        LogDatabaseReady(logger);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "Banco local migrado e pronto para uso.")]
    private static partial void LogDatabaseReady(ILogger logger);
}
