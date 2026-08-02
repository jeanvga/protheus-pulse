using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Service.Configuration;

namespace ProtheusPulse.Service.HostedServices;

/// <summary>
/// Mantém as amostras de processador, memória e disco do servidor sempre frescas.
/// A aba Servidor lê o último valor coletado em vez de consultar o sistema
/// operacional a cada requisição.
/// </summary>
public sealed partial class ServerResourceWorker(
    IServerResourceMonitor monitor,
    PulseOptions options,
    ILogger<ServerResourceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SampleSafely();
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.ServerSampleIntervalSeconds, 2, 300));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            SampleSafely();
        }
    }

    private void SampleSafely()
    {
        try
        {
            _ = monitor.Sample();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogSampleFailure(logger, exception);
        }
    }

    [LoggerMessage(EventId = 1601, Level = LogLevel.Warning, Message = "Falha controlada ao amostrar os recursos do servidor.")]
    private static partial void LogSampleFailure(ILogger logger, Exception exception);
}
