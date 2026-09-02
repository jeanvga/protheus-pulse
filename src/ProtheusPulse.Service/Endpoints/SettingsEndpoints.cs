using System.IdentityModel.Tokens.Jwt;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Configuration;
using ProtheusPulse.Service.Monitoring;
using ProtheusPulse.Service.Observability;

namespace ProtheusPulse.Service.Endpoints;

/// <summary>
/// Dados de envio de e-mail da aba Configurações. A senha entra, mas nunca sai:
/// o GET informa apenas se existe uma senha guardada.
/// </summary>
public static class SettingsEndpoints
{
    private const int MaximumRecipients = 20;

    public static RouteGroupBuilder MapEmailSettings(this RouteGroupBuilder api)
    {
        api.MapGet("/settings/email", GetAsync).RequireAuthorization("Administrator");
        api.MapPut("/settings/email", SaveAsync).RequireAuthorization("Administrator");
        api.MapPost("/settings/email/test", SendTestAsync).RequireAuthorization("Administrator").RequireRateLimiting("serviceControl");
        api.MapGet("/settings/server-thresholds", GetServerThresholdsAsync).RequireAuthorization("Administrator");
        api.MapPut("/settings/server-thresholds", SaveServerThresholdsAsync).RequireAuthorization("Administrator");
        api.MapGet("/settings/retention", GetRetentionAsync).RequireAuthorization("Administrator");
        api.MapPut("/settings/retention", SaveRetentionAsync).RequireAuthorization("Administrator");
        api.MapGet("/settings/network", GetNetworkAsync).RequireAuthorization("Administrator");
        api.MapPut("/settings/network", SaveNetworkAsync).RequireAuthorization("Administrator");
        return api;
    }

    /// <summary>
    /// Onde o painel escuta. O padrão é loopback; abrir para a rede é opt-in explícito
    /// porque o tráfego é HTTP puro e a tela administra serviços do Windows.
    /// </summary>
    private static IResult GetNetworkAsync(IConfiguration configuration)
    {
        var options = configuration.GetSection(NetworkOptions.SectionName).Get<NetworkOptions>() ?? new NetworkOptions();
        return Results.Ok(new NetworkSettingsResponse(
            options.AllowRemoteAccess,
            options.Port,
            options.BuildUrl(),
            LocalAddresses(options.Port)));
    }

    private static async Task<IResult> SaveNetworkAsync(
        SaveNetworkRequest request,
        PulseDataDirectory dataDirectory,
        CancellationToken cancellationToken)
    {
        var options = new NetworkOptions { AllowRemoteAccess = request.AllowRemoteAccess, Port = request.Port };
        if (options.Validate() is { Count: > 0 } errors)
        {
            return Results.BadRequest(new { message = string.Join(" ", errors) });
        }

        var path = Path.Combine(dataDirectory.Path, "network.json");
        var document = new Dictionary<string, NetworkOptions>(StringComparer.Ordinal)
        {
            [NetworkOptions.SectionName] = options
        };
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(document, NetworkSerializerOptions),
            cancellationToken);
        return Results.Ok(new NetworkSettingsResponse(
            options.AllowRemoteAccess,
            options.Port,
            options.BuildUrl(),
            LocalAddresses(options.Port)));
    }

    /// <summary>Endereços que o operador pode digitar em outra máquina.</summary>
    private static string[] LocalAddresses(int port)
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up
                    && item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => $"http://{item.Address}:{port}")
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToArray();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions NetworkSerializerOptions = new() { WriteIndented = true };

    public sealed record SaveNetworkRequest(bool AllowRemoteAccess, int Port);

    /// <summary>
    /// Aplica os limites gravados às opções que os coletores já têm em mãos, para o ajuste
    /// valer no ciclo seguinte em vez de exigir reinício do serviço.
    /// </summary>
    public static void Apply(ServerThresholdSetting stored, ServerResourceOptions server, ProbeCollectorOptions probes)
    {
        server.CpuWarningPercent = stored.CpuWarningPercent;
        server.CpuCriticalPercent = stored.CpuCriticalPercent;
        server.MemoryWarningPercent = stored.MemoryWarningPercent;
        server.MemoryCriticalPercent = stored.MemoryCriticalPercent;
        server.DiskWarningPercent = stored.DiskFreeWarningPercent;
        server.DiskCriticalPercent = stored.DiskFreeCriticalPercent;
        probes.DiskWarningPercent = stored.DiskFreeWarningPercent;
        probes.DiskCriticalPercent = stored.DiskFreeCriticalPercent;
    }

    private static async Task<IResult> GetServerThresholdsAsync(
        PulseDbContext dbContext,
        ServerResourceOptions options,
        CancellationToken cancellationToken)
    {
        var stored = await dbContext.ServerThresholdSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return Results.Ok(new ServerThresholdResponse(
            stored?.CpuWarningPercent ?? options.CpuWarningPercent,
            stored?.CpuCriticalPercent ?? options.CpuCriticalPercent,
            stored?.MemoryWarningPercent ?? options.MemoryWarningPercent,
            stored?.MemoryCriticalPercent ?? options.MemoryCriticalPercent,
            stored?.DiskFreeWarningPercent ?? options.DiskWarningPercent,
            stored?.DiskFreeCriticalPercent ?? options.DiskCriticalPercent,
            stored?.UpdatedAt));
    }

    private static async Task<IResult> SaveServerThresholdsAsync(
        ServerThresholdRequest request,
        PulseDbContext dbContext,
        ServerResourceOptions serverOptions,
        ProbeCollectorOptions probeOptions,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.CpuWarningPercent is <= 0 or > 100 || request.CpuCriticalPercent is <= 0 or > 100
            || request.CpuWarningPercent >= request.CpuCriticalPercent)
        {
            errors["cpu"] = ["A atenção do processador deve ficar entre 1 e 100 e abaixo do crítico."];
        }

        if (request.MemoryWarningPercent is <= 0 or > 100 || request.MemoryCriticalPercent is <= 0 or > 100
            || request.MemoryWarningPercent >= request.MemoryCriticalPercent)
        {
            errors["memory"] = ["A atenção da memória deve ficar entre 1 e 100 e abaixo do crítico."];
        }

        // Disco é medido pelo espaço livre: o crítico fica abaixo da atenção.
        if (request.DiskFreeWarningPercent is < 0 or > 100 || request.DiskFreeCriticalPercent is < 0 or > 100
            || request.DiskFreeCriticalPercent >= request.DiskFreeWarningPercent)
        {
            errors["disk"] = ["O espaço livre crítico deve ficar entre 0 e 100 e abaixo do de atenção."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var stored = await dbContext.ServerThresholdSettings.FirstOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            stored = new ServerThresholdSetting();
            dbContext.ServerThresholdSettings.Add(stored);
        }

        stored.CpuWarningPercent = request.CpuWarningPercent;
        stored.CpuCriticalPercent = request.CpuCriticalPercent;
        stored.MemoryWarningPercent = request.MemoryWarningPercent;
        stored.MemoryCriticalPercent = request.MemoryCriticalPercent;
        stored.DiskFreeWarningPercent = request.DiskFreeWarningPercent;
        stored.DiskFreeCriticalPercent = request.DiskFreeCriticalPercent;
        stored.UpdatedAt = clock.UtcNow;
        AddAudit(dbContext, clock, principal, httpContext, "ServerThresholdsUpdated", stored.Id, new
        {
            stored.CpuWarningPercent,
            stored.CpuCriticalPercent,
            stored.MemoryWarningPercent,
            stored.MemoryCriticalPercent,
            stored.DiskFreeWarningPercent,
            stored.DiskFreeCriticalPercent
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        Apply(stored, serverOptions, probeOptions);
        return Results.Ok(new ServerThresholdResponse(
            stored.CpuWarningPercent,
            stored.CpuCriticalPercent,
            stored.MemoryWarningPercent,
            stored.MemoryCriticalPercent,
            stored.DiskFreeWarningPercent,
            stored.DiskFreeCriticalPercent,
            stored.UpdatedAt));
    }

    public sealed record ServerThresholdRequest(
        double CpuWarningPercent,
        double CpuCriticalPercent,
        double MemoryWarningPercent,
        double MemoryCriticalPercent,
        double DiskFreeWarningPercent,
        double DiskFreeCriticalPercent);

    public sealed record ServerThresholdResponse(
        double CpuWarningPercent,
        double CpuCriticalPercent,
        double MemoryWarningPercent,
        double MemoryCriticalPercent,
        double DiskFreeWarningPercent,
        double DiskFreeCriticalPercent,
        DateTimeOffset? UpdatedAt);

    public sealed record NetworkSettingsResponse(
        bool AllowRemoteAccess,
        int Port,
        string BoundUrl,
        IReadOnlyList<string> LocalAddresses);

    /// <summary>
    /// Quanto tempo o histórico fica no SQLite. Sem isso o banco cresce sem teto no
    /// servidor do cliente, e o valor só existia no appsettings, fora do alcance da tela.
    /// </summary>
    private static async Task<IResult> GetRetentionAsync(
        PulseDbContext dbContext,
        PulseOptions options,
        CancellationToken cancellationToken)
    {
        var stored = await dbContext.RetentionSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var counts = new RetentionCounts(
            await dbContext.ProbeResults.CountAsync(cancellationToken),
            await dbContext.LogEvents.CountAsync(cancellationToken),
            await dbContext.MetricSamples.CountAsync(cancellationToken));
        return Results.Ok(new RetentionSettingsResponse(
            stored?.HistoryRetentionDays ?? options.HistoryRetentionDays,
            stored?.MetricAggregationAfterDays ?? options.MetricAggregationAfterDays,
            stored?.UpdatedAt,
            counts));
    }

    private static async Task<IResult> SaveRetentionAsync(
        SaveRetentionRequest request,
        PulseDbContext dbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request.HistoryRetentionDays is < 1 or > 365)
        {
            return Results.BadRequest(new { message = "O histórico deve ficar entre 1 e 365 dias." });
        }

        if (request.MetricAggregationAfterDays < 1 || request.MetricAggregationAfterDays > request.HistoryRetentionDays)
        {
            return Results.BadRequest(new { message = "A agregação deve ficar entre 1 dia e o tamanho do histórico." });
        }

        var stored = await dbContext.RetentionSettings.FirstOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            stored = new RetentionSetting();
            dbContext.RetentionSettings.Add(stored);
        }

        stored.HistoryRetentionDays = request.HistoryRetentionDays;
        stored.MetricAggregationAfterDays = request.MetricAggregationAfterDays;
        stored.UpdatedAt = clock.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { stored.HistoryRetentionDays, stored.MetricAggregationAfterDays, stored.UpdatedAt });
    }

    public sealed record SaveRetentionRequest(int HistoryRetentionDays, int MetricAggregationAfterDays);

    public sealed record RetentionCounts(int ProbeResults, int LogEvents, int MetricSamples);

    public sealed record RetentionSettingsResponse(
        int HistoryRetentionDays,
        int MetricAggregationAfterDays,
        DateTimeOffset? UpdatedAt,
        RetentionCounts Counts);

    private static async Task<IResult> GetAsync(
        PulseDbContext dbContext,
        NotificationConfigurationProtector protector,
        CancellationToken cancellationToken)
    {
        var channel = await EmailSettingsAccess.FindChannelAsync(dbContext, cancellationToken);
        var settings = ReadSettings(channel, protector);
        return Results.Ok(new EmailSettingsResponse(
            channel is not null,
            channel?.Enabled ?? false,
            settings?.Host ?? string.Empty,
            settings?.Port ?? 587,
            settings?.Security ?? SmtpSecurity.Auto,
            settings?.Username,
            !string.IsNullOrEmpty(settings?.Password),
            settings?.FromAddress ?? string.Empty,
            settings?.FromName,
            settings?.Recipients ?? [],
            settings?.TimeoutSeconds ?? 20,
            settings?.AllowInvalidCertificate ?? false,
            settings?.NotifyAlerts ?? true,
            settings?.NotifyLogErrors ?? true));
    }

    private static async Task<IResult> SaveAsync(
        SaveEmailSettingsRequest request,
        PulseDbContext dbContext,
        NotificationConfigurationProtector protector,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var recipients = (request.Recipients ?? [])
            .Select(item => item?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var errors = Validate(request, recipients);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var channel = await EmailSettingsAccess.FindChannelAsync(dbContext, cancellationToken);
        var current = ReadSettings(channel, protector);
        // Senha ausente no corpo significa "mantenha a que já está guardada"; string
        // vazia significa "apague". Assim a tela nunca precisa reexibir o segredo.
        var password = request.Password is null ? current?.Password : NullIfEmpty(request.Password);
        var settings = new SmtpSettings
        {
            Host = request.Host!.Trim(),
            Port = request.Port,
            Security = request.Security ?? SmtpSecurity.Auto,
            Username = NullIfEmpty(request.Username),
            Password = password,
            FromAddress = request.FromAddress!.Trim(),
            FromName = NullIfEmpty(request.FromName),
            Recipients = recipients,
            TimeoutSeconds = request.TimeoutSeconds,
            AllowInvalidCertificate = request.AllowInvalidCertificate,
            NotifyAlerts = request.NotifyAlerts,
            NotifyLogErrors = request.NotifyLogErrors
        };

        if (channel is null)
        {
            channel = new NotificationChannel
            {
                Name = EmailSettingsAccess.ChannelName,
                Type = NotificationChannelType.Smtp,
                Enabled = request.Enabled
            };
            dbContext.NotificationChannels.Add(channel);
        }
        else
        {
            channel.Enabled = request.Enabled;
        }

        channel.ProtectedConfiguration = protector.Protect(new NotificationChannelConfiguration(Smtp: settings));
        AddAudit(dbContext, clock, principal, httpContext, "EmailSettingsUpdated", channel.Id, new
        {
            settings.Port,
            security = settings.Security.ToString(),
            channel.Enabled,
            recipientCount = recipients.Length,
            hasCredentials = !string.IsNullOrEmpty(settings.Username),
            settings.AllowInvalidCertificate
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SendTestAsync(
        PulseDbContext dbContext,
        NotificationConfigurationProtector protector,
        EmailSender emailSender,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var channel = await EmailSettingsAccess.FindChannelAsync(dbContext, cancellationToken);
        var settings = ReadSettings(channel, protector);
        if (channel is null || settings is null)
        {
            return Results.Conflict(new { message = "Salve os dados de envio antes de testar." });
        }

        var result = await emailSender.SendAsync(
            settings,
            "[Protheus Pulse] Teste de envio",
            $"""
             Este é um teste de configuração do Protheus Pulse.

             Servidor: {settings.Host}:{settings.Port} ({settings.Security})
             Enviado em {EmailSettingsAccess.FormatTimestamp(clock.UtcNow)}

             Se você recebeu esta mensagem, os alertas e os erros de log chegarão por aqui.
             """,
            cancellationToken);
        AddAudit(dbContext, clock, principal, httpContext, "EmailSettingsTested", channel.Id, new { result.Success });
        await dbContext.SaveChangesAsync(cancellationToken);
        return result.Success
            ? Results.Ok(new { result.Success, result.Message })
            : Results.Json(new { result.Success, result.Message }, statusCode: StatusCodes.Status502BadGateway);
    }

    private static SmtpSettings? ReadSettings(NotificationChannel? channel, NotificationConfigurationProtector protector)
    {
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

    private static Dictionary<string, string[]> Validate(SaveEmailSettingsRequest request, IReadOnlyList<string> recipients)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!IsValidHost(request.Host)) errors["host"] = ["Informe o servidor SMTP, sem espaços."];
        if (request.Port is < 1 or > 65_535) errors["port"] = ["A porta deve estar entre 1 e 65535."];
        if (request.Security.HasValue && !Enum.IsDefined(request.Security.Value)) errors["security"] = ["Modo de segurança inválido."];
        if (!IsValidAddress(request.FromAddress)) errors["fromAddress"] = ["Informe um remetente válido."];
        if (request.FromName is not null && !IsValidText(request.FromName, 120)) errors["fromName"] = ["O nome do remetente deve ter até 120 caracteres."];
        if (request.Username is not null && !IsValidText(request.Username, 200)) errors["username"] = ["O usuário deve ter até 200 caracteres."];
        if (request.Password is not null && request.Password.Length > 200) errors["password"] = ["A senha deve ter até 200 caracteres."];
        if (request.TimeoutSeconds is < 5 or > 120) errors["timeoutSeconds"] = ["O tempo limite deve estar entre 5 e 120 segundos."];
        if (recipients.Count == 0 || recipients.Count > MaximumRecipients)
        {
            errors["recipients"] = [$"Informe de 1 a {MaximumRecipients} destinatários."];
        }
        else if (recipients.Any(item => !IsValidAddress(item)))
        {
            errors["recipients"] = ["Há um endereço de destinatário inválido."];
        }

        return errors;
    }

    private static bool IsValidHost(string? value)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && trimmed.Length <= 253
            && !trimmed.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));
    }

    private static bool IsValidAddress(string? value)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && trimmed.Length <= 254
            && MailboxAddress.TryParse(trimmed, out _);
    }

    private static bool IsValidText(string? value, int maximumLength)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && trimmed.Length <= maximumLength && !trimmed.Any(char.IsControl);
    }

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static void AddAudit(
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        string action,
        Guid entityId,
        object details)
    {
        var userClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = Guid.TryParse(userClaim, out var userId) ? userId : null,
            Action = action,
            EntityType = nameof(NotificationChannel),
            EntityId = entityId.ToString(),
            SanitizedDetailsJson = AuditDetails.Serialize(details),
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = clock.UtcNow
        });
    }

    public sealed record EmailSettingsResponse(
        bool Configured,
        bool Enabled,
        string Host,
        int Port,
        SmtpSecurity Security,
        string? Username,
        bool HasPassword,
        string FromAddress,
        string? FromName,
        IReadOnlyList<string> Recipients,
        int TimeoutSeconds,
        bool AllowInvalidCertificate,
        bool NotifyAlerts,
        bool NotifyLogErrors);

    public sealed record SaveEmailSettingsRequest(
        bool Enabled,
        string? Host,
        int Port,
        SmtpSecurity? Security,
        string? Username,
        string? Password,
        string? FromAddress,
        string? FromName,
        IReadOnlyList<string?>? Recipients,
        int TimeoutSeconds,
        bool AllowInvalidCertificate,
        bool NotifyAlerts,
        bool NotifyLogErrors);
}
