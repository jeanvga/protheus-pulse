using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Configuration;
using ProtheusPulse.Service.Endpoints;
using ProtheusPulse.Service.HostedServices;
using ProtheusPulse.Service.Hubs;
using ProtheusPulse.Service.Monitoring;
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

var builder = WebApplication.CreateBuilder(args);
var demoMode = args.Any(item => string.Equals(item, "--demo", StringComparison.OrdinalIgnoreCase))
    || builder.Configuration.GetValue<bool>("Pulse:DemoMode");

var pulseOptions = builder.Configuration.GetSection(PulseOptions.SectionName).Get<PulseOptions>() ?? new PulseOptions();
if (pulseOptions.HistoryRetentionDays is < 1 or > 365
    || pulseOptions.MetricAggregationAfterDays is < 1
    || pulseOptions.MetricAggregationAfterDays > pulseOptions.HistoryRetentionDays
    || pulseOptions.CollectionIntervalSeconds is < 10 or > 3_600
    || pulseOptions.CollectorTimeoutSeconds is < 1 or > 120
    || pulseOptions.MaximumConcurrentCollectors is < 1 or > 16
    || pulseOptions.MaximumLogBytesPerCycle is < 4_096 or > 1_048_576
    || pulseOptions.DiskCriticalPercent is < 0 or > 100
    || pulseOptions.DiskWarningPercent is < 0 or > 100
    || pulseOptions.DiskCriticalPercent >= pulseOptions.DiskWarningPercent)
{
    throw new InvalidOperationException("A seção Pulse possui limites de coleta inválidos.");
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
builder.Services.AddSingleton(new ProbeCollectorOptions
{
    MaximumLogBytesPerCycle = pulseOptions.MaximumLogBytesPerCycle,
    DiskWarningPercent = pulseOptions.DiskWarningPercent,
    DiskCriticalPercent = pulseOptions.DiskCriticalPercent
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
builder.Services.AddHealthChecks().AddDbContextCheck<PulseDbContext>("sqlite", tags: readinessTags);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<MonitoringWorker>();
builder.Services.AddSingleton<RetentionService>();
builder.Services.AddSingleton<RetentionWorker>();
builder.Services.AddScoped<AlertEngine>();
builder.Services.AddSingleton<NotificationConfigurationProtector>();
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

if (demoMode)
{
    builder.Services.AddHostedService<DemoPulseWorker>();
}
else
{
    builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MonitoringWorker>());
    builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<RetentionWorker>());
}

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
    await dbContext.Database.MigrateAsync();
    if (demoMode)
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IDemoDataSeeder>();
        await seeder.SeedAsync(CancellationToken.None);
    }
}

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
    await next();
});
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
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();

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
