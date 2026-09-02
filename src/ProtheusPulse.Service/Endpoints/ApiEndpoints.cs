using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Application.Security;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Demo;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.HostedServices;
using ProtheusPulse.Service.Security;

namespace ProtheusPulse.Service.Endpoints;

public static class ApiEndpoints
{
    /// <summary>Versão do pacote, usada no diagnóstico e no rodapé do painel.</summary>
    private static readonly string ProductVersion =
        typeof(ApiEndpoints).Assembly.GetName().Version?.ToString(3) ?? "development";

    private static readonly string[] DiagnosticNotes = ["Nenhum caminho monitorado ou segredo é exposto neste diagnóstico."];
    private static readonly string[] UsernameRequired = ["Informe o nome de usuário."];

    public static IEndpointRouteBuilder MapPulseApi(this IEndpointRouteBuilder endpoints, bool demoMode)
    {
        var api = endpoints.MapGroup("/api/v1");

        api.MapGet("/auth/status", async (PulseDbContext db, CancellationToken cancellationToken) => Results.Ok(new
        {
            requiresSetup = !await db.Users.AnyAsync(cancellationToken),
            demoMode,
            demoUsername = demoMode ? DemoDataSeeder.DemoUsername : null,
            demoPassword = demoMode ? DemoDataSeeder.DemoPassword : null,
            version = ProductVersion
        })).AllowAnonymous();

        api.MapPost("/auth/setup", SetupAsync).AllowAnonymous().RequireRateLimiting("authentication");
        api.MapPost("/auth/login", LoginAsync).AllowAnonymous().RequireRateLimiting("authentication");
        api.MapGet("/auth/me", (ClaimsPrincipal principal) => Results.Ok(new
        {
            username = principal.FindFirstValue("unique_name"),
            displayName = principal.Identity?.Name,
            role = principal.FindFirstValue(ClaimTypes.Role)
        })).RequireAuthorization("Viewer");

        // A sessão vale oito horas e não se renovava: ela caía sem aviso e levava junto o
        // formulário aberto. A tela renova sozinha antes do fim, enquanto a pessoa usa.
        api.MapPost("/auth/refresh", async (
            ClaimsPrincipal principal,
            PulseDbContext db,
            ITokenService tokenService,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue("sub");
            if (!Guid.TryParse(userId, out var id))
            {
                return Results.Unauthorized();
            }

            // Conta desativada ou removida durante a sessão não ganha token novo.
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(tokenService.Create(user));
        }).RequireAuthorization("Viewer");

        api.MapGet("/dashboard/summary", async (IDashboardQuery query, CancellationToken cancellationToken) =>
            Results.Ok(await query.GetSummaryAsync(demoMode, cancellationToken))).RequireAuthorization("Viewer");

        api.MapGet("/installations", async (IDashboardQuery query, CancellationToken cancellationToken) =>
            Results.Ok(await query.GetInstallationsAsync(cancellationToken))).RequireAuthorization("Viewer");
        api.MapInstallationManagement();
        api.MapInstallationImport();
        api.MapDiscovery();
        api.MapOperations();
        api.MapServiceControl(demoMode);
        api.MapAutomation();
        api.MapHeartbeats();
        api.MapServerResources();
        api.MapEmailSettings();
        api.MapUsers();

        api.MapGet("/components", async (IDashboardQuery query, CancellationToken cancellationToken) =>
            Results.Ok(await query.GetComponentsAsync(cancellationToken))).RequireAuthorization("Viewer");

        api.MapGet("/checks", async (PulseDbContext db, CancellationToken cancellationToken) => Results.Ok(await db.ProbeResults
            .AsNoTracking()
            .OrderByDescending(item => item.ObservedAt)
            .Take(100)
            .Select(item => new { item.Id, item.ComponentId, item.ProbeType, item.Status, item.ObservedAt, item.DurationMs, item.Message })
            .ToListAsync(cancellationToken))).RequireAuthorization("Viewer");

        // O resumo do painel corta em oito ocorrências para caber na tela inicial. Aqui a
        // consulta é a tabela inteira: sem isso o histórico ficava inalcançável depois da
        // nona ocorrência, embora estivesse todo gravado.
        api.MapGet("/alerts", async (
            PulseDbContext db,
            string? state,
            Guid? componentId,
            DateTimeOffset? from,
            int? take,
            int? skip,
            CancellationToken cancellationToken) =>
        {
            var scoped = db.AlertOccurrences.AsNoTracking();
            if (componentId is { } component)
            {
                scoped = scoped.Where(item => item.AlertRule.ComponentId == component);
            }

            if (from is { } start)
            {
                scoped = scoped.Where(item => item.StartedAt >= start);
            }

            // A contagem por estado ignora o estado selecionado: ela alimenta os próprios
            // botões de filtro, que precisam mostrar quanto há em cada um.
            var byState = await scoped
                .GroupBy(item => item.State)
                .Select(group => new { State = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);

            var filtered = scoped;
            if (!string.IsNullOrWhiteSpace(state)
                && !string.Equals(state, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse<AlertState>(state, ignoreCase: true, out var parsed))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["state"] = ["Informe Active, Acknowledged, Resolved, Silenced ou all."]
                    });
                }

                filtered = filtered.Where(item => item.State == parsed);
            }

            var total = await filtered.CountAsync(cancellationToken);
            var items = await filtered
                .OrderBy(item => item.State == AlertState.Resolved)
                .ThenByDescending(item => item.StartedAt)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, 200))
                .Select(item => new
                {
                    item.Id,
                    item.CorrelationId,
                    InstallationName = item.AlertRule.Component.Installation.Name,
                    ComponentName = item.AlertRule.Component.Name,
                    RuleName = item.AlertRule.Name,
                    item.AlertRule.Severity,
                    item.State,
                    item.StartedAt,
                    item.ResolvedAt,
                    item.Evidence
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                Total = total,
                ByState = byState.ToDictionary(entry => entry.State.ToString(), entry => entry.Count),
                Items = items
            });
        }).RequireAuthorization("Viewer");

        // O filtro roda no banco: procurar dentro das duzentas linhas já carregadas
        // encontrava apenas o que estava na tela, e o histórico guarda trinta dias.
        api.MapGet("/log-events", async (
            PulseDbContext db,
            string? search,
            string? level,
            Guid? componentId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? take,
            int? skip,
            CancellationToken cancellationToken) =>
        {
            var query = db.LogEvents.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(level) && !string.Equals(level, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(item => item.Level == level);
            }

            if (componentId is { } component)
            {
                query = query.Where(item => item.ComponentId == component);
            }

            if (from is { } start)
            {
                query = query.Where(item => item.ObservedAt >= start);
            }

            if (to is { } end)
            {
                query = query.Where(item => item.ObservedAt <= end);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                // LIKE no SQLite ignora maiúsculas em ASCII; os curingas do usuário são
                // escapados para que "100%" procure o texto e não qualquer coisa.
                var term = search.Trim()
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("%", "\\%", StringComparison.Ordinal)
                    .Replace("_", "\\_", StringComparison.Ordinal);
                var pattern = $"%{term}%";
                query = query.Where(item => EF.Functions.Like(item.Message, pattern, "\\")
                    || EF.Functions.Like(item.Component.Name, pattern, "\\")
                    || EF.Functions.Like(item.Component.Installation.Name, pattern, "\\"));
            }

            var byLevel = await query
                .GroupBy(item => item.Level)
                .Select(group => new { Level = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
            var items = await query
                .OrderByDescending(item => item.ObservedAt)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(item => new
                {
                    item.Id,
                    item.ComponentId,
                    InstallationName = item.Component.Installation.Name,
                    ComponentName = item.Component.Name,
                    item.ObservedAt,
                    item.Level,
                    item.Message,
                    item.OccurrenceCount,
                    item.ThreadId,
                    item.User,
                    item.Computer,
                    item.SourceFile,
                    item.SourceLine,
                    item.Environment,
                    item.Company,
                    item.Module,
                    item.Routine,
                    item.Detail
                })
                .ToListAsync(cancellationToken);
            return Results.Ok(new
            {
                Total = byLevel.Sum(entry => entry.Count),
                ByLevel = byLevel.ToDictionary(entry => entry.Level, entry => entry.Count),
                Items = items
            });
        }).RequireAuthorization("Viewer");

        // A auditoria já era gravada em toda ação administrativa e não tinha como ser lida:
        // o painel mostrava um texto fixo no lugar dela.
        api.MapGet("/audit", async (
            PulseDbContext db,
            string? search,
            string? action,
            DateTimeOffset? from,
            int? take,
            int? skip,
            CancellationToken cancellationToken) =>
        {
            var query = db.AuditEvents.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(action) && !string.Equals(action, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(item => item.Action == action);
            }

            if (from is { } start)
            {
                query = query.Where(item => item.OccurredAt >= start);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim()
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("%", "\\%", StringComparison.Ordinal)
                    .Replace("_", "\\_", StringComparison.Ordinal);
                var pattern = $"%{term}%";
                query = query.Where(item => EF.Functions.Like(item.Action, pattern, "\\")
                    || EF.Functions.Like(item.EntityType, pattern, "\\")
                    || (item.User != null && EF.Functions.Like(item.User.DisplayName, pattern, "\\"))
                    || (item.User != null && EF.Functions.Like(item.User.Username, pattern, "\\")));
            }

            var byAction = await query
                .GroupBy(item => item.Action)
                .Select(group => new { Action = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
            var items = await query
                .OrderByDescending(item => item.OccurredAt)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, 200))
                .Select(item => new
                {
                    item.Id,
                    item.Action,
                    item.EntityType,
                    item.EntityId,
                    item.OccurredAt,
                    item.RemoteAddress,
                    Details = item.SanitizedDetailsJson,
                    UserDisplayName = item.User != null ? item.User.DisplayName : null,
                    Username = item.User != null ? item.User.Username : null
                })
                .ToListAsync(cancellationToken);
            return Results.Ok(new
            {
                Total = byAction.Sum(entry => entry.Count),
                ByAction = byAction.OrderByDescending(entry => entry.Count).ToDictionary(entry => entry.Action, entry => entry.Count),
                Items = items
            });
        }).RequireAuthorization("Administrator");

        api.MapGet("/maintenance-windows", async (PulseDbContext db, CancellationToken cancellationToken) => Results.Ok(await db.MaintenanceWindows
            .AsNoTracking()
            .OrderByDescending(item => item.StartsAt)
            .Select(item => new
            {
                item.Id,
                item.InstallationId,
                item.ComponentId,
                item.Name,
                item.StartsAt,
                item.EndsAt,
                item.Reason,
                InstallationName = item.Installation != null ? item.Installation.Name : item.Component!.Installation.Name,
                ComponentName = item.Component != null ? item.Component.Name : null
            })
            .ToListAsync(cancellationToken))).RequireAuthorization("Viewer");

        api.MapGet("/diagnostics", async (PulseDbContext db, CancellationToken cancellationToken) => Results.Ok(new
        {
            service = "Protheus Pulse",
            status = await db.Database.CanConnectAsync(cancellationToken) ? "Healthy" : "Critical",
            database = "SQLite",
            demoMode,
            platform = Environment.OSVersion.Platform.ToString(),
            version = ProductVersion,
            notes = DiagnosticNotes
        })).RequireAuthorization("Administrator");

        api.MapPost("/diagnostics/collect-now", async (MonitoringWorker worker, CancellationToken cancellationToken) =>
        {
            if (demoMode)
            {
                return Results.Conflict(new { message = "A coleta real fica desabilitada no modo demonstração." });
            }

            var processedComponents = await worker.RunNowAsync(cancellationToken);
            return Results.Ok(new { processedComponents, completedAt = DateTimeOffset.UtcNow });
        }).RequireAuthorization("Administrator").RequireRateLimiting("serviceControl");

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
