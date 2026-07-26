using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.Service.Endpoints;

/// <summary>
/// Marcações por instalação: qual ambiente é o exclusivo do modo manutenção e
/// quais ambientes o watchdog pode religar sozinho.
/// </summary>
public static class AutomationEndpoints
{
    public static RouteGroupBuilder MapAutomation(this RouteGroupBuilder api)
    {
        api.MapPost("/installations/{installationId:guid}/exclusive", SetExclusiveAsync)
            .RequireAuthorization("Administrator")
            .RequireRateLimiting("serviceControl");
        api.MapPost("/installations/{installationId:guid}/auto-start", SetAutoStartAsync)
            .RequireAuthorization("Administrator")
            .RequireRateLimiting("serviceControl");
        return api;
    }

    private static async Task<IResult> SetExclusiveAsync(
        Guid installationId,
        AutomationFlagRequest? request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var installation = await FindAsync(dbContext, installationId, cancellationToken);
        if (installation is null)
        {
            return Results.NotFound(new { message = "Instalação não encontrada." });
        }

        if (installation.IsDemo)
        {
            return Results.Conflict(new { message = "Dados demonstrativos não podem ser alterados." });
        }

        var enabled = request?.Enabled ?? false;
        if (enabled)
        {
            // Apenas uma instalação fica exclusiva; o modo manutenção precisa de um
            // único ambiente no ar para compilar e salvar configurações.
            var others = await dbContext.Installations
                .Where(item => item.IsExclusive && item.Id != installationId)
                .ToListAsync(cancellationToken);
            foreach (var other in others)
            {
                other.IsExclusive = false;
            }
        }

        installation.IsExclusive = enabled;
        AddAudit(dbContext, clock, principal, httpContext, "ExclusiveInstallationChanged", installation, new
        {
            enabled,
            installation = installation.Name
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(installation));
    }

    private static async Task<IResult> SetAutoStartAsync(
        Guid installationId,
        AutomationFlagRequest? request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var installation = await FindAsync(dbContext, installationId, cancellationToken);
        if (installation is null)
        {
            return Results.NotFound(new { message = "Instalação não encontrada." });
        }

        if (installation.IsDemo)
        {
            return Results.Conflict(new { message = "Dados demonstrativos não podem ser alterados." });
        }

        installation.AutoStartEnabled = request?.Enabled ?? false;
        AddAudit(dbContext, clock, principal, httpContext, "AutoStartSettingChanged", installation, new
        {
            enabled = installation.AutoStartEnabled,
            installation = installation.Name
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(installation));
    }

    private static Task<Installation?> FindAsync(
        PulseDbContext dbContext,
        Guid installationId,
        CancellationToken cancellationToken) =>
        dbContext.Installations.SingleOrDefaultAsync(item => item.Id == installationId, cancellationToken);

    private static AutomationFlagResponse ToResponse(Installation installation) =>
        new(installation.Id, installation.Name, installation.IsExclusive, installation.AutoStartEnabled);

    private static void AddAudit(
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        string action,
        Installation installation,
        object details)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = GetUserId(principal),
            Action = action,
            EntityType = nameof(Installation),
            EntityId = installation.Id.ToString(),
            SanitizedDetailsJson = JsonSerializer.Serialize(details),
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = clock.UtcNow
        });
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public sealed record AutomationFlagRequest(bool Enabled);

    public sealed record AutomationFlagResponse(Guid Id, string Name, bool IsExclusive, bool AutoStartEnabled);
}
