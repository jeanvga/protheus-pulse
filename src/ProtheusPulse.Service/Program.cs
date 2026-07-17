using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
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
using ProtheusPulse.Service.Security;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
var demoMode = args.Any(item => string.Equals(item, "--demo", StringComparison.OrdinalIgnoreCase))
    || builder.Configuration.GetValue<bool>("Pulse:DemoMode");

var configuredDataDirectory = builder.Configuration["Pulse:DataDirectory"];
var dataDirectory = !string.IsNullOrWhiteSpace(configuredDataDirectory)
    ? Path.GetFullPath(configuredDataDirectory)
    : builder.Environment.IsDevelopment() || demoMode
        ? Path.Combine(builder.Environment.ContentRootPath, ".data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProtheusPulse");
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(Path.Combine(dataDirectory, "logs"));

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

var securityOptions = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
securityOptions.JwtSigningKey = Environment.GetEnvironmentVariable("PULSE_JWT_SIGNING_KEY") ?? securityOptions.JwtSigningKey;
if (string.IsNullOrWhiteSpace(securityOptions.JwtSigningKey))
{
    if (!demoMode && !builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Defina PULSE_JWT_SIGNING_KEY com pelo menos 32 caracteres antes de executar fora do modo demo/desenvolvimento.");
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

builder.Services.AddRateLimiter(options => options.AddPolicy("authentication", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1),
            AutoReplenishment = true
        })));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
string[] readinessTags = ["ready"];
builder.Services.AddHealthChecks().AddDbContextCheck<PulseDbContext>("sqlite", tags: readinessTags);
builder.Services.AddEndpointsApiExplorer();
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

public partial class Program;
