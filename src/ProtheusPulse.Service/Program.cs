using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Configuration;
using ProtheusPulse.Service.Endpoints;
using ProtheusPulse.Service.HostedServices;
using ProtheusPulse.Service.Hubs;
using ProtheusPulse.Service.Monitoring;
using ProtheusPulse.Service.Observability;
using ProtheusPulse.Service.Security;
using ProtheusPulse.Service.WindowsSetup;
using Serilog;
using Serilog.Events;

var installerExitCode = await WindowsServiceInstaller.TryRunAsync(args);
if (installerExitCode.HasValue)
{
    Environment.ExitCode = installerExitCode.Value;
    return;
}

AppDomain.CurrentDomain.UnhandledException += static (_, eventArgs) =>
    TryLogStartupCrash(eventArgs.ExceptionObject as Exception);

var builder = WebApplication.CreateBuilder(args);
var demoMode = args.Any(item => string.Equals(item, "--demo", StringComparison.OrdinalIgnoreCase))
    || builder.Configuration.GetValue<bool>("Pulse:DemoMode");

var pulseOptions = builder.Configuration.GetSection(PulseOptions.SectionName).Get<PulseOptions>() ?? new PulseOptions();
if (pulseOptions.HistoryRetentionDays is < 1 or > 365
    || pulseOptions.MetricAggregationAfterDays is < 1
    || pulseOptions.MetricAggregationAfterDays > pulseOptions.HistoryRetentionDays
    || pulseOptions.CollectionIntervalSeconds is < 10 or > 3_600
    || pulseOptions.CollectorTimeoutSeconds is < 1 or > 120
    || pulseOptions.AutoStartIntervalSeconds is < 15 or > 3_600
    || pulseOptions.MaximumConcurrentCollectors is < 1 or > 16
    || pulseOptions.MaximumLogBytesPerCycle is < 4_096 or > 1_048_576
    || pulseOptions.DiskCriticalPercent is < 0 or > 100
    || pulseOptions.DiskWarningPercent is < 0 or > 100
    || pulseOptions.DiskCriticalPercent >= pulseOptions.DiskWarningPercent
    || pulseOptions.ServerSampleIntervalSeconds is < 2 or > 300
    || pulseOptions.ServerHistorySamples is < 10 or > 2_880
    || pulseOptions.CpuWarningPercent is <= 0 or > 100
    || pulseOptions.CpuCriticalPercent is <= 0 or > 100
    || pulseOptions.CpuWarningPercent >= pulseOptions.CpuCriticalPercent
    || pulseOptions.MemoryWarningPercent is <= 0 or > 100
    || pulseOptions.MemoryCriticalPercent is <= 0 or > 100
    || pulseOptions.MemoryWarningPercent >= pulseOptions.MemoryCriticalPercent
    || pulseOptions.LogAlertDigestSeconds is < 10 or > 3_600)
{
    throw new InvalidOperationException("A seção Pulse possui limites de coleta inválidos.");
}

var observabilityOptions = builder.Configuration
    .GetSection(ObservabilityOptions.SectionName)
    .Get<ObservabilityOptions>() ?? new ObservabilityOptions();
var observabilityErrors = observabilityOptions.Validate();
if (observabilityErrors.Count > 0)
{
    throw new InvalidOperationException(string.Join(" ", observabilityErrors));
}

var configuredDataDirectory = pulseOptions.DataDirectory;
var dataDirectory = !string.IsNullOrWhiteSpace(configuredDataDirectory)
    ? Path.GetFullPath(configuredDataDirectory)
    : builder.Environment.IsDevelopment() || demoMode
        ? Path.Combine(builder.Environment.ContentRootPath, ".data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProtheusPulse");
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(Path.Combine(dataDirectory, "logs"));
var keysDirectory = Path.Combine(dataDirectory, "keys");
Directory.CreateDirectory(keysDirectory);

// Acesso pela rede: fica em arquivo próprio no diretório de dados para a tela poder
// gravar sem tocar no appsettings instalado em Program Files, que é somente leitura
// para o serviço. O bind só muda no próximo start do serviço.
var networkSettingsPath = Path.Combine(dataDirectory, "network.json");
builder.Configuration.AddJsonFile(networkSettingsPath, optional: true, reloadOnChange: false);
var networkOptions = builder.Configuration.GetSection(NetworkOptions.SectionName).Get<NetworkOptions>() ?? new NetworkOptions();
if (networkOptions.Validate() is { Count: > 0 } networkErrors)
{
    throw new InvalidOperationException(string.Join(" ", networkErrors));
}

// Sobrescreve a chave do Kestrel, não UseUrls: endpoint declarado em
// Kestrel:Endpoints tem precedência sobre UseUrls, e o serviço continuaria preso
// ao 127.0.0.1 do appsettings mesmo com o acesso remoto ligado na tela.
builder.Configuration.AddInMemoryCollection(networkOptions.BuildOverrides());
builder.Services.AddSingleton(new PulseDataDirectory(dataDirectory));

builder.Host.UseWindowsService(options => options.ServiceName = "ProtheusPulse");
builder.Host.UseSerilog((_, _, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .WriteTo.File(
        Path.Combine(dataDirectory, "logs", "pulse-.log"),
        formatProvider: CultureInfo.InvariantCulture,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 25 * 1024 * 1024,
        rollOnFileSizeLimit: true));

var connectionString = builder.Configuration.GetConnectionString("PulseDb") ?? "Data Source={DataDirectory}/pulse.db;Cache=Shared";
connectionString = connectionString.Replace("{DataDirectory}", dataDirectory.Replace("\\", "/"), StringComparison.Ordinal);
builder.Services.AddPulseInfrastructure(connectionString);
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("ProtheusPulse")
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
}
builder.Services.AddSingleton(pulseOptions);
builder.Services.AddSingleton(observabilityOptions);
builder.Services.AddSingleton<PulseTelemetry>();
if (observabilityOptions.Enabled)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(
                serviceName: "protheus-pulse",
                serviceNamespace: observabilityOptions.ServiceNamespace,
                serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString(),
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(
            [
                new KeyValuePair<string, object>("host.name", Environment.MachineName)
            ]))
        .WithMetrics(metrics => metrics
            .AddMeter(PulseTelemetry.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter((exporter, metricReader) =>
            {
                exporter.Endpoint = observabilityOptions.GetMetricsEndpoint();
                exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                metricReader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                    checked(observabilityOptions.ExportIntervalSeconds * 1_000);
            }));
}
builder.Services.AddSingleton(new ProbeCollectorOptions
{
    MaximumLogBytesPerCycle = pulseOptions.MaximumLogBytesPerCycle,
    DiskWarningPercent = pulseOptions.DiskWarningPercent,
    DiskCriticalPercent = pulseOptions.DiskCriticalPercent
});
builder.Services.AddSingleton(new ServerResourceOptions
{
    CpuWarningPercent = pulseOptions.CpuWarningPercent,
    CpuCriticalPercent = pulseOptions.CpuCriticalPercent,
    MemoryWarningPercent = pulseOptions.MemoryWarningPercent,
    MemoryCriticalPercent = pulseOptions.MemoryCriticalPercent,
    DiskWarningPercent = pulseOptions.DiskWarningPercent,
    DiskCriticalPercent = pulseOptions.DiskCriticalPercent,
    HistorySamples = pulseOptions.ServerHistorySamples
});

var securityOptions = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
securityOptions.JwtSigningKey = Environment.GetEnvironmentVariable("PULSE_JWT_SIGNING_KEY")
    ?? ReadJwtSigningKeyFile(Environment.GetEnvironmentVariable("PULSE_JWT_SIGNING_KEY_FILE"))
    ?? securityOptions.JwtSigningKey;
if (string.IsNullOrWhiteSpace(securityOptions.JwtSigningKey))
{
    if (!demoMode && !builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Defina PULSE_JWT_SIGNING_KEY ou PULSE_JWT_SIGNING_KEY_FILE com pelo menos 32 bytes antes de executar fora do modo demo/desenvolvimento.");
    }

    securityOptions.JwtSigningKey = "DEMO-ONLY-Protheus-Pulse-signing-key-2026-change-me";
}

if (Encoding.UTF8.GetByteCount(securityOptions.JwtSigningKey) < 32)
{
    throw new InvalidOperationException("Security:JwtSigningKey deve possuir pelo menos 32 bytes.");
}

builder.Services.AddSingleton(securityOptions);
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = securityOptions.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = securityOptions.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(securityOptions.JwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/pulse"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Viewer", policy => policy.RequireRole(UserRole.Viewer.ToString(), UserRole.Operator.ToString(), UserRole.Administrator.ToString()))
    .AddPolicy("Operator", policy => policy.RequireRole(UserRole.Operator.ToString(), UserRole.Administrator.ToString()))
    .AddPolicy("Administrator", policy => policy.RequireRole(UserRole.Administrator.ToString()));

// Compressão: o painel é servido pelo próprio serviço e, com acesso remoto ligado,
// o pacote atravessa a rede do cliente. O JS passa de ~290 KB para ~85 KB.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json", "image/svg+xml"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true
            }));
    // Ações que mexem em serviços Windows: mesmo restritas a Administrator, um
    // token vazado ou um clique repetido não podem virar uma enxurrada de
    // start/stop no SCM. O particionamento é por origem porque o limiter roda
    // antes da autenticação no pipeline.
    options.AddPolicy("serviceControl", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true
            }));
    options.AddPolicy("heartbeat", context =>
    {
        var origin = context.Connection.RemoteIpAddress?.ToString() ?? "local";
        var jobKey = context.Request.RouteValues["jobKey"]?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{origin}:{jobKey}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true
            });
    });
});
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
string[] readinessTags = ["ready"];
builder.Services.AddSingleton<DatabaseReadyState>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PulseDbContext>("sqlite", tags: readinessTags)
    // Enquanto a migração roda, o serviço já responde ao SCM mas não está pronto:
    // é este check que o instalador espera antes de dar a instalação por concluída.
    .AddCheck<DatabaseMigrationHealthCheck>("migration", tags: readinessTags);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<MonitoringWorker>();
builder.Services.AddSingleton<RetentionService>();
builder.Services.AddSingleton<RetentionWorker>();
builder.Services.AddScoped<AlertEngine>();
builder.Services.AddSingleton<ServiceActionCoordinator>();
builder.Services.AddSingleton<NotificationConfigurationProtector>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<LogAlertMailBuffer>();
builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Protheus Pulse API",
        Version = "v1",
        Description = "API local, independente e somente leitura para observabilidade de ambientes TOTVS Protheus."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
    });
});

builder.Services.AddHostedService(serviceProvider => new DatabaseInitializer(
    serviceProvider,
    demoMode,
    serviceProvider.GetRequiredService<DatabaseReadyState>(),
    serviceProvider.GetRequiredService<ILogger<DatabaseInitializer>>()));
// Processador, memória e disco são do próprio servidor e a leitura é inofensiva,
// então a aba Servidor também funciona na demonstração.
builder.Services.AddHostedService<ServerResourceWorker>();
// Os agentes de log podem enviar erros em qualquer modo; o digest depende só do SMTP.
builder.Services.AddHostedService<LogAlertMailWorker>();
if (demoMode)
{
    builder.Services.AddHostedService<DemoPulseWorker>();
}
else
{
    builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MonitoringWorker>());
    builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<RetentionWorker>());
    builder.Services.AddHostedService<AutoStartWorker>();
}

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
});
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; connect-src 'self' ws: wss:; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "accelerometer=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
    context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    await next();
});
app.UseResponseCompression();
app.UseSerilogRequestLogging();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || demoMode)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("ready") }).AllowAnonymous();
app.MapPulseApi(demoMode);
app.MapHub<PulseHub>("/hubs/pulse");
// O build do frontend carimba o hash no nome do arquivo, então o conteúdo de /assets
// nunca muda para uma mesma URL e pode ficar no cache do navegador. O index.html
// continua sem cache — inclusive na rota de fallback, que tem opções próprias: sem
// isso o navegador serviria a página antiga apontando para um asset que já não existe.
var staticFileOptions = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var path = context.Context.Request.Path.Value ?? string.Empty;
        context.Context.Response.Headers.CacheControl = path.StartsWith("/assets/", StringComparison.Ordinal)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    }
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFileOptions);
app.MapFallbackToFile("index.html", staticFileOptions).AllowAnonymous();

await app.RunAsync();

static void TryLogStartupCrash(Exception? exception)
{
    if (exception is null)
    {
        return;
    }

    try
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ProtheusPulse",
            "logs");
        Directory.CreateDirectory(logDirectory);
        File.AppendAllText(
            Path.Combine(logDirectory, "startup-crash.log"),
            $"{DateTimeOffset.UtcNow:O} {exception}{Environment.NewLine}",
            new UTF8Encoding(false));
    }
    catch (Exception writeException)
        when (writeException is IOException or UnauthorizedAccessException or System.Security.SecurityException)
    {
        // Sem acesso ao diretório de dados; o erro original ainda sobe para o console e o SCM.
    }
}

static string? ReadJwtSigningKeyFile(string? configuredPath)
{
    if (string.IsNullOrWhiteSpace(configuredPath))
    {
        return null;
    }

    try
    {
        var path = Path.GetFullPath(configuredPath);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 32 or > 1_024 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("O arquivo configurado em PULSE_JWT_SIGNING_KEY_FILE é inválido.");
        }

        var value = File.ReadAllText(path, Encoding.UTF8).Trim();
        return string.IsNullOrEmpty(value)
            ? throw new InvalidOperationException("O arquivo configurado em PULSE_JWT_SIGNING_KEY_FILE está vazio.")
            : value;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
        throw new InvalidOperationException("Não foi possível ler com segurança o arquivo configurado em PULSE_JWT_SIGNING_KEY_FILE.", exception);
    }
}

public partial class Program;
