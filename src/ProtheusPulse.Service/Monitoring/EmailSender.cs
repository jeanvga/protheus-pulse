using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Service.Monitoring;

/// <summary>
/// Dados do servidor de e-mail. Ficam cifrados com DataProtection dentro do canal
/// de notificação do tipo SMTP; a senha nunca volta pela API.
/// </summary>
public sealed record SmtpSettings
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public SmtpSecurity Security { get; init; } = SmtpSecurity.Auto;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string FromAddress { get; init; } = string.Empty;
    public string? FromName { get; init; }
    public IReadOnlyList<string> Recipients { get; init; } = [];
    public int TimeoutSeconds { get; init; } = 20;

    /// <summary>
    /// Aceita certificado que não valida na cadeia. Só faz sentido em relay interno
    /// com certificado próprio, e por isso vem desligado.
    /// </summary>
    public bool AllowInvalidCertificate { get; init; }

    public bool NotifyAlerts { get; init; } = true;
    public bool NotifyLogErrors { get; init; } = true;
}

public sealed record EmailDeliveryResult(bool Success, string Message);

public sealed partial class EmailSender(ILogger<EmailSender> logger)
{
    public async Task<EmailDeliveryResult> SendAsync(
        SmtpSettings settings,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var validation = Validate(settings);
        if (validation is not null)
        {
            return new EmailDeliveryResult(false, validation);
        }

        try
        {
            if (!await IsRoutableAsync(settings.Host, cancellationToken))
            {
                return new EmailDeliveryResult(false, "O servidor SMTP resolveu apenas para endereços bloqueados.");
            }

            using var message = BuildMessage(settings, subject, body);
            using var client = new SmtpClient
            {
                Timeout = Math.Clamp(settings.TimeoutSeconds, 5, 120) * 1_000
            };
            // Sem a permissão explícita, um certificado que não valida derruba o envio.
            client.ServerCertificateValidationCallback = (_, _, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None || settings.AllowInvalidCertificate;
            await client.ConnectAsync(settings.Host, settings.Port, ResolveSocketOptions(settings.Security), cancellationToken);
            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return new EmailDeliveryResult(true, $"Mensagem entregue a {settings.Recipients.Count} destinatário(s).");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDeliveryFailure(logger, exception);
            return new EmailDeliveryResult(false, Describe(exception));
        }
    }

    private static string? Validate(SmtpSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            return "Informe o servidor SMTP.";
        }

        if (settings.Port is < 1 or > 65_535)
        {
            return "A porta SMTP deve estar entre 1 e 65535.";
        }

        if (string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            return "Informe o remetente.";
        }

        return settings.Recipients.Count == 0 ? "Informe ao menos um destinatário." : null;
    }

    private static MimeMessage BuildMessage(SmtpSettings settings, string subject, string body)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName ?? "Protheus Pulse", settings.FromAddress));
        foreach (var recipient in settings.Recipients)
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        return message;
    }

    private static SecureSocketOptions ResolveSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto
    };

    /// <summary>
    /// Mesma checagem aplicada aos webhooks: o destino precisa resolver para ao menos
    /// um endereço utilizável antes de abrirmos a conexão.
    /// </summary>
    private static async Task<bool> IsRoutableAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return SafeNetworkConnector.IsAllowed(literal);
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        return addresses.Any(SafeNetworkConnector.IsAllowed);
    }

    private static string Describe(Exception exception) => exception switch
    {
        SslHandshakeException => "Falha no handshake TLS. Confira a porta e o modo de segurança.",
        SmtpCommandException command => $"O servidor SMTP recusou a mensagem ({command.StatusCode}).",
        SmtpProtocolException => "Erro de protocolo na conversa com o servidor SMTP.",
        FormatException => "Há um endereço de e-mail inválido na configuração.",
        _ => "Não foi possível concluir o envio. Confira endereço, porta, segurança e credenciais."
    };

    [LoggerMessage(EventId = 1701, Level = LogLevel.Warning, Message = "Falha controlada ao enviar e-mail pelo canal SMTP.")]
    private static partial void LogDeliveryFailure(ILogger logger, Exception exception);
}

/// <summary>
/// Leitura da configuração SMTP guardada como canal de notificação. Existe um único
/// canal do tipo SMTP: ele é o "dados para envio de e-mail" da aba Configurações.
/// </summary>
public static class EmailSettingsAccess
{
    public const string ChannelName = "E-mail (SMTP)";

    public static Task<NotificationChannel?> FindChannelAsync(
        PulseDbContext dbContext,
        CancellationToken cancellationToken) =>
        dbContext.NotificationChannels
            .OrderBy(item => item.Name)
            .FirstOrDefaultAsync(item => item.Type == NotificationChannelType.Smtp, cancellationToken);

    /// <summary>Devolve a configuração apenas quando o canal existe e está habilitado.</summary>
    public static async Task<SmtpSettings?> LoadEnabledAsync(
        PulseDbContext dbContext,
        NotificationConfigurationProtector protector,
        CancellationToken cancellationToken)
    {
        var channel = await dbContext.NotificationChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Type == NotificationChannelType.Smtp && item.Enabled, cancellationToken);
        if (channel is null || string.IsNullOrEmpty(channel.ProtectedConfiguration))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(channel.ProtectedConfiguration).Smtp;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("dd/MM/yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
