using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Configuration;
using ProtheusPulse.Service.Monitoring;

namespace ProtheusPulse.Service.HostedServices;

public sealed record LogAlertNotice(
    string InstallationName,
    string ComponentName,
    string Level,
    string Message,
    int OccurrenceCount,
    DateTimeOffset ObservedAt);

/// <summary>
/// Fila em memória entre a ingestão dos agentes e o e-mail. É limitada de
/// propósito: se o AppServer despejar milhares de erros, o Pulse descarta os mais
/// antigos em vez de estourar a memória ou segurar a requisição do agente.
/// </summary>
public sealed class LogAlertMailQueue
{
    private const int Capacity = 500;

    private readonly Channel<LogAlertNotice> channel = Channel.CreateBounded<LogAlertNotice>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public void Enqueue(LogAlertNotice notice) => channel.Writer.TryWrite(notice);

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) =>
        channel.Reader.WaitToReadAsync(cancellationToken);

    public bool TryRead([MaybeNullWhen(false)] out LogAlertNotice notice) => channel.Reader.TryRead(out notice);
}

/// <summary>
/// Junta os erros recebidos dos agentes durante uma janela e manda um e-mail só.
/// Um e-mail por linha de log seria inútil no primeiro incidente de verdade.
/// </summary>
public sealed partial class LogAlertMailWorker(
    IServiceScopeFactory scopeFactory,
    LogAlertMailQueue queue,
    EmailSender emailSender,
    NotificationConfigurationProtector protector,
    PulseOptions options,
    ILogger<LogAlertMailWorker> logger) : BackgroundService
{
    private const int MaximumEventsPerDigest = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await queue.WaitToReadAsync(stoppingToken))
                {
                    return;
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Clamp(options.LogAlertDigestSeconds, 10, 3_600)),
                    stoppingToken);
                var notices = Drain();
                if (notices.Count > 0)
                {
                    await SendDigestAsync(notices, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDigestFailure(logger, exception);
            }
        }
    }

    private List<LogAlertNotice> Drain()
    {
        var notices = new List<LogAlertNotice>();
        while (notices.Count < MaximumEventsPerDigest && queue.TryRead(out var notice))
        {
            notices.Add(notice);
        }

        return notices;
    }

    private async Task SendDigestAsync(IReadOnlyList<LogAlertNotice> notices, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var settings = await EmailSettingsAccess.LoadEnabledAsync(dbContext, protector, cancellationToken);
        if (settings is null || !settings.NotifyLogErrors)
        {
            return;
        }

        var components = notices
            .Select(item => $"{item.InstallationName} · {item.ComponentName}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var occurrences = notices.Sum(item => item.OccurrenceCount);
        var subject = components.Length == 1
            ? $"[Protheus Pulse] {occurrences} erro(s) no log de {notices[0].ComponentName}"
            : $"[Protheus Pulse] {occurrences} erro(s) de log em {components.Length} componentes";

        var body = new StringBuilder()
            .AppendLine("Erros encontrados nos logs enviados pelos agentes:")
            .AppendLine();
        foreach (var group in notices.GroupBy(item => $"{item.InstallationName} · {item.ComponentName}", StringComparer.Ordinal))
        {
            body.AppendLine(group.Key);
            foreach (var notice in group.OrderByDescending(item => item.ObservedAt))
            {
                body.Append("  [").Append(notice.Level).Append("] ")
                    .Append(notice.OccurrenceCount.ToString(CultureInfo.InvariantCulture)).Append("x · ")
                    .AppendLine(EmailSettingsAccess.FormatTimestamp(notice.ObservedAt))
                    .Append("  ").AppendLine(notice.Message);
            }

            body.AppendLine();
        }

        body.Append("Total de ").Append(occurrences.ToString(CultureInfo.InvariantCulture))
            .Append(" ocorrência(s) em ").Append(notices.Count.ToString(CultureInfo.InvariantCulture))
            .AppendLine(" mensagem(ns) distinta(s).")
            .AppendLine("As mensagens já estão registradas na página de Logs do Pulse.");
        var result = await emailSender.SendAsync(settings, subject, body.ToString(), cancellationToken);
        if (!result.Success)
        {
            LogDigestRejected(logger, result.Message);
        }
    }

    [LoggerMessage(EventId = 1702, Level = LogLevel.Warning, Message = "Falha controlada ao montar o e-mail de erros de log.")]
    private static partial void LogDigestFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1703, Level = LogLevel.Warning, Message = "O e-mail de erros de log não foi entregue: {Reason}")]
    private static partial void LogDigestRejected(ILogger logger, string reason);
}
