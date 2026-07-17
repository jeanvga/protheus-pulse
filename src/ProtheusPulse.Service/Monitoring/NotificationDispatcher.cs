using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Service.Monitoring;

public sealed partial class NotificationDispatcher : IDisposable
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly NotificationConfigurationProtector protector;
    private readonly ILogger<NotificationDispatcher> logger;
    private readonly HttpClient client;

    public NotificationDispatcher(
        IServiceScopeFactory scopeFactory,
        NotificationConfigurationProtector protector,
        ILogger<NotificationDispatcher> logger)
    {
        this.scopeFactory = scopeFactory;
        this.protector = protector;
        this.logger = logger;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = static (context, cancellationToken) =>
                new ValueTask<Stream>(SafeNetworkConnector.ConnectStreamAsync(
                    context.DnsEndPoint.Host,
                    context.DnsEndPoint.Port,
                    cancellationToken))
        };
        client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task DispatchAsync(
        IReadOnlyList<AlertTransition> transitions,
        CancellationToken cancellationToken)
    {
        if (transitions.Count == 0)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var channels = await dbContext.NotificationChannels
            .AsNoTracking()
            .Where(item => item.Enabled && item.Type != NotificationChannelType.Dashboard)
            .Take(20)
            .ToListAsync(cancellationToken);
        foreach (var channel in channels)
        {
            NotificationChannelConfiguration configuration;
            try
            {
                configuration = protector.Unprotect(channel.ProtectedConfiguration);
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException)
            {
                LogInvalidConfiguration(logger, channel.Id, exception);
                continue;
            }

            foreach (var transition in transitions)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, configuration.Url)
                    {
                        Content = JsonContent.Create(CreatePayload(channel.Type, transition))
                    };
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        LogDeliveryRejected(logger, channel.Id, (int)response.StatusCode);
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or OperationCanceledException)
                {
                    LogDeliveryFailure(logger, channel.Id, exception);
                }
            }
        }
    }

    public void Dispose()
    {
        client.Dispose();
        GC.SuppressFinalize(this);
    }

    private static object CreatePayload(NotificationChannelType type, AlertTransition transition)
    {
        var text = $"Protheus Pulse: alerta {transition.Kind} ({transition.Severity}) · correlação {transition.CorrelationId:N}";
        return type switch
        {
            NotificationChannelType.Slack => new { text },
            NotificationChannelType.Discord => new { content = text },
            NotificationChannelType.Teams => new { type = "message", text },
            _ => new
            {
                source = "ProtheusPulse",
                eventType = transition.Kind.ToString(),
                transition.CorrelationId,
                severity = transition.Severity.ToString(),
                state = transition.State.ToString()
            }
        };
    }

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning, Message = "Configuração protegida inválida no canal {ChannelId}.")]
    private static partial void LogInvalidConfiguration(ILogger logger, Guid channelId, Exception exception);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Warning, Message = "Canal {ChannelId} recusou a notificação com HTTP {StatusCode}.")]
    private static partial void LogDeliveryRejected(ILogger logger, Guid channelId, int statusCode);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Warning, Message = "Falha controlada ao enviar notificação pelo canal {ChannelId}.")]
    private static partial void LogDeliveryFailure(ILogger logger, Guid channelId, Exception exception);
}

public sealed class NotificationConfigurationProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("ProtheusPulse.NotificationChannels.v1");

    public string Protect(NotificationChannelConfiguration configuration) =>
        protector.Protect(JsonSerializer.Serialize(configuration));

    public NotificationChannelConfiguration Unprotect(string protectedConfiguration) =>
        JsonSerializer.Deserialize<NotificationChannelConfiguration>(protector.Unprotect(protectedConfiguration))
        ?? throw new JsonException("Configuração protegida vazia.");
}

public sealed record NotificationChannelConfiguration(string Url);
