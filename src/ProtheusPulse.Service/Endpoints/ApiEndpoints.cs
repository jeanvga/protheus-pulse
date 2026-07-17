using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Application.Security;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Demo;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Security;

namespace ProtheusPulse.Service.Endpoints;

public static class ApiEndpoints
{
    private static readonly string[] DiagnosticNotes = ["Nenhum caminho monitorado ou segredo é exposto neste diagnóstico."];
    private static readonly string[] PostMethods = ["POST"];
    private static readonly string[] UsernameRequired = ["Informe o nome de usuário."];

    public static IEndpointRouteBuilder MapPulseApi(this IEndpointRouteBuilder endpoints, bool demoMode)
    {
        var api = endpoints.MapGroup("/api/v1");

        api.MapGet("/auth/status", async (PulseDbContext db, CancellationToken cancellationToken) => Results.Ok(new
        {
            requiresSetup = !await db.Users.AnyAsync(cancellationToken),
            demoMode,
            demoUsername = demoMode ? DemoDataSeeder.DemoUsername : null,
            demoPassword = demoMode ? DemoDataSeeder.DemoPassword : null
        })).AllowAnonymous();

        api.MapPost("/auth/setup", SetupAsync).AllowAnonymous().RequireRateLimiting("authentication");
        api.MapPost("/auth/login", LoginAsync).AllowAnonymous().RequireRateLimiting("authentication");
        api.MapGet("/auth/me", (ClaimsPrincipal principal) => Results.Ok(new
        {
            username = principal.FindFirstValue("unique_name"),
            displayName = principal.Identity?.Name,
            role = principal.FindFirstValue(ClaimTypes.Role)
        })).RequireAuthorization("Viewer");

        api.MapGet("/dashboard/summary", async (IDashboardQuery query, CancellationToken cancellationToken) =>
            Results.Ok(await query.GetSummaryAsync(demoMode, cancellationToken))).RequireAuthorization("Viewer");

        api.MapGet("/installations", async (IDashboardQuery query, CancellationToken cancellationToken) =>
            Results.Ok(await query.GetInstallationsAsync(cancellationToken))).RequireAuthorization("Viewer");
        api.MapInstallationManagement();
        api.MapInstallationImport();
        api.MapDiscovery();

        api.MapGet("/components", async (IDashboardQuery query, CancellationToken cancellationToken) =>
            Results.Ok(await query.GetComponentsAsync(cancellationToken))).RequireAuthorization("Viewer");

        api.MapGet("/checks", async (PulseDbContext db, CancellationToken cancellationToken) => Results.Ok(await db.ProbeResults
            .AsNoTracking()
            .OrderByDescending(item => item.ObservedAt)
            .Take(100)
            .Select(item => new { item.Id, item.ComponentId, item.ProbeType, item.Status, item.ObservedAt, item.DurationMs, item.Message })
            .ToListAsync(cancellationToken))).RequireAuthorization("Viewer");

        api.MapGet("/alerts", async (IDashboardQuery query, CancellationToken cancellationToken) =>
        {
            var dashboard = await query.GetSummaryAsync(demoMode, cancellationToken);
            return Results.Ok(dashboard.Alerts);
        }).RequireAuthorization("Viewer");

        api.MapGet("/log-events", () => Results.Ok(new { items = Array.Empty<object>(), phase = 3, message = "Coleta incremental será habilitada na Fase 3." }))
            .RequireAuthorization("Viewer");

        api.MapGet("/maintenance-windows", async (PulseDbContext db, CancellationToken cancellationToken) => Results.Ok(await db.MaintenanceWindows
            .AsNoTracking()
            .Select(item => new { item.Id, item.InstallationId, item.ComponentId, item.Name, item.StartsAt, item.EndsAt, item.Reason })
            .ToListAsync(cancellationToken))).RequireAuthorization("Viewer");

        api.MapGet("/diagnostics", async (PulseDbContext db, CancellationToken cancellationToken) => Results.Ok(new
        {
            service = "Protheus Pulse",
            status = await db.Database.CanConnectAsync(cancellationToken) ? "Healthy" : "Critical",
            database = "SQLite",
            demoMode,
            platform = Environment.OSVersion.Platform.ToString(),
            version = typeof(ApiEndpoints).Assembly.GetName().Version?.ToString() ?? "development",
            notes = DiagnosticNotes
        })).RequireAuthorization("Administrator");

        api.MapMethods("/heartbeats/{jobKey}", PostMethods, (string jobKey) => Results.Json(new
        {
            jobKey,
            message = "A ingestão autenticada de heartbeats será habilitada na Fase 5."
        }, statusCode: StatusCodes.Status501NotImplemented)).AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> SetupAsync(
        SetupRequest request,
        PulseDbContext dbContext,
        IPasswordService passwordService,
        IClock clock,
        ITokenService tokenService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            return Results.Conflict(new { message = "A configuração inicial já foi concluída." });
        }

        var password = request.Password ?? string.Empty;
        var errors = PasswordPolicy.Validate(password);
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 120 || errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["username"] = string.IsNullOrWhiteSpace(request.Username) ? UsernameRequired : Array.Empty<string>(),
                ["password"] = errors.ToArray()
            });
        }

        var user = new User
        {
            Username = request.Username.Trim().ToLowerInvariant(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Username.Trim() : request.DisplayName.Trim(),
            PasswordHash = passwordService.Hash(password),
            Role = UserRole.Administrator,
            CreatedAt = clock.UtcNow,
            IsActive = true
        };
        dbContext.Users.Add(user);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = user.Id,
            Action = "InitialAdministratorCreated",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(tokenService.Create(user));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        PulseDbContext dbContext,
        IPasswordService passwordService,
        IClock clock,
        ITokenService tokenService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Results.Json(new { message = "Usuário ou senha inválidos." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var normalizedUsername = request.Username.Trim().ToLowerInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.Username == normalizedUsername && item.IsActive, cancellationToken);
        var verified = user is not null && passwordService.Verify(request.Password, user.PasswordHash);
        if (!verified)
        {
            if (user is null)
            {
                _ = passwordService.Hash(request.Password);
            }
            dbContext.AuditEvents.Add(new AuditEvent
            {
                UserId = user?.Id,
                Action = "LoginFailed",
                EntityType = nameof(User),
                EntityId = user?.Id.ToString(),
                RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                OccurredAt = clock.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Json(new { message = "Usuário ou senha inválidos." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        user!.LastLoginAt = clock.UtcNow;
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = user.Id,
            Action = "LoginSucceeded",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(tokenService.Create(user));
    }

    public sealed record LoginRequest(string? Username, string? Password);
    public sealed record SetupRequest(string? Username, string? DisplayName, string? Password);
}
