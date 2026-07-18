using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Configuration;

namespace ProtheusPulse.Service.Endpoints;

public static class InstallationEndpoints
{
    public static RouteGroupBuilder MapInstallationManagement(this RouteGroupBuilder api)
    {
        api.MapGet("/installations/{installationId:guid}/configuration", GetConfigurationAsync)
            .RequireAuthorization("Administrator");
        api.MapPost("/installations", CreateAsync).RequireAuthorization("Administrator");
        api.MapPut("/installations/{installationId:guid}", UpdateAsync).RequireAuthorization("Administrator");
        api.MapDelete("/installations/{installationId:guid}", DeleteAsync).RequireAuthorization("Administrator");
        return api;
    }

    private static async Task<IResult> GetConfigurationAsync(
        Guid installationId,
        PulseDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var installation = await ConfigurationQuery(dbContext)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == installationId, cancellationToken);
        return installation is null
            ? Results.NotFound(new { message = "Instalação não encontrada." })
            : Results.Ok(ToResponse(installation));
    }

    private static async Task<IResult> CreateAsync(
        SaveInstallationRequest request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var existingNames = await dbContext.Installations
            .AsNoTracking()
            .Select(item => item.Name)
            .ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Name)
            && existingNames.Contains(request.Name.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return Results.Conflict(new { message = "Já existe uma instalação com esse nome." });
        }

        var errors = Validate(request, existingNames, existingComponentIds: null);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["configuration"] = errors.ToArray() });
        }

        var now = clock.UtcNow;
        var installation = new Installation
        {
            Name = request.Name!.Trim(),
            Environment = request.Environment!.Value,
            CustomEnvironmentName = request.Environment == EnvironmentKind.Custom
                ? request.CustomEnvironmentName!.Trim()
                : null,
            TagsJson = SerializeTags(request.Tags),
            IsDemo = false,
            CreatedAt = now
        };
        foreach (var componentRequest in request.Components!)
        {
            var component = CreateComponent(componentRequest!);
            installation.Components.Add(component);
        }

        dbContext.Installations.Add(installation);
        AddAudit(dbContext, principal, httpContext, now, "InstallationCreated", installation, request);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created(
            $"/api/v1/installations/{installation.Id}",
            new InstallationCreatedResponse(
                installation.Id,
                installation.Name,
                installation.Environment,
                installation.CustomEnvironmentName,
                DeserializeTags(installation.TagsJson),
                installation.Components.Count,
                HealthStatus.Unknown));
    }

    private static async Task<IResult> UpdateAsync(
        Guid installationId,
        SaveInstallationRequest request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var installation = await ConfigurationQuery(dbContext)
            .SingleOrDefaultAsync(item => item.Id == installationId, cancellationToken);
        if (installation is null)
        {
            return Results.NotFound(new { message = "Instalação não encontrada." });
        }

        if (installation.IsDemo)
        {
            return Results.Conflict(new { message = "Dados demonstrativos não podem ser alterados." });
        }

        var existingNames = await dbContext.Installations
            .AsNoTracking()
            .Where(item => item.Id != installationId)
            .Select(item => item.Name)
            .ToListAsync(cancellationToken);
        var existingComponentIds = installation.Components.Select(item => item.Id).ToHashSet();
        var errors = Validate(request, existingNames, existingComponentIds);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["configuration"] = errors.ToArray() });
        }

        installation.Name = request.Name!.Trim();
        installation.Environment = request.Environment!.Value;
        installation.CustomEnvironmentName = request.Environment == EnvironmentKind.Custom
            ? request.CustomEnvironmentName!.Trim()
            : null;
        installation.TagsJson = SerializeTags(request.Tags);

        var requestedComponents = request.Components!;
        var retainedIds = requestedComponents
            .Where(item => item!.Id.HasValue)
            .Select(item => item!.Id!.Value)
            .ToHashSet();
        foreach (var removedComponent in installation.Components.Where(item => !retainedIds.Contains(item.Id)).ToArray())
        {
            dbContext.Components.Remove(removedComponent);
        }

        foreach (var componentRequest in requestedComponents)
        {
            var validRequest = componentRequest!;
            if (validRequest.Id.HasValue)
            {
                var component = installation.Components.Single(item => item.Id == validRequest.Id.Value);
                ApplyComponent(dbContext, component, validRequest);
            }
            else
            {
                var component = CreateComponent(validRequest);
                installation.Components.Add(component);
                dbContext.Components.Add(component);
            }
        }

        AddAudit(dbContext, principal, httpContext, clock.UtcNow, "InstallationUpdated", installation, request);
        await dbContext.SaveChangesAsync(cancellationToken);

        var saved = await ConfigurationQuery(dbContext)
            .AsNoTracking()
            .SingleAsync(item => item.Id == installationId, cancellationToken);
        return Results.Ok(ToResponse(saved));
    }

    private static async Task<IResult> DeleteAsync(
        Guid installationId,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var installation = await dbContext.Installations.SingleOrDefaultAsync(item => item.Id == installationId, cancellationToken);
        if (installation is null)
        {
            return Results.NotFound(new { message = "Instalação não encontrada." });
        }

        if (installation.IsDemo)
        {
            return Results.Conflict(new { message = "Dados demonstrativos não podem ser removidos." });
        }

        dbContext.Installations.Remove(installation);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = GetUserId(principal),
            Action = "InstallationDeleted",
            EntityType = nameof(Installation),
            EntityId = installationId.ToString(),
            SanitizedDetailsJson = "{}",
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static IQueryable<Installation> ConfigurationQuery(PulseDbContext dbContext) =>
        dbContext.Installations
            .AsSplitQuery()
            .Include(item => item.Components).ThenInclude(item => item.WindowsServiceTargets)
            .Include(item => item.Components).ThenInclude(item => item.ProcessTargets)
            .Include(item => item.Components).ThenInclude(item => item.FileTargets)
            .Include(item => item.Components).ThenInclude(item => item.LogSources)
            .Include(item => item.Components).ThenInclude(item => item.TcpChecks)
            .Include(item => item.Components).ThenInclude(item => item.HttpChecks);

    private static List<string> Validate(
        SaveInstallationRequest request,
        IReadOnlyCollection<string> existingNames,
        HashSet<Guid>? existingComponentIds)
    {
        var document = new InstallationImportDocument
        {
            SchemaVersion = 1,
            Installations = [ToDefinition(request)]
        };
        var validation = InstallationImportValidator.Validate(document, existingNames);
        var errors = validation.Errors.ToList();
        var components = request.Components;
        if (components is not null)
        {
            if (components.Any(item => item is null))
            {
                errors.Add("Informe os dados do componente.");
            }

            var duplicateNames = components
                .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item!.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);
            if (duplicateNames)
            {
                errors.Add("Os nomes dos componentes devem ser únicos dentro da instalação.");
            }

            foreach (var component in components.Where(item => item is not null))
            {
                if (existingComponentIds is null && component!.Id.HasValue)
                {
                    errors.Add("Novos componentes não podem informar um identificador existente.");
                }
                else if (existingComponentIds is not null
                    && component!.Id.HasValue
                    && !existingComponentIds.Contains(component.Id.Value))
                {
                    errors.Add("Um componente informado não pertence a esta instalação.");
                }
            }
        }

        return errors.Distinct(StringComparer.Ordinal).Take(100).ToList();
    }

    private static InstallationDefinition ToDefinition(SaveInstallationRequest request) => new()
    {
        Name = request.Name,
        Environment = request.Environment?.ToString(),
        CustomEnvironmentName = request.CustomEnvironmentName,
        Tags = request.Tags?.ToList(),
        Components = request.Components?.Select(component => component is null ? null : new ComponentDefinition
        {
            Name = component.Name,
            Type = component.Type?.ToString(),
            IsRequired = component.IsRequired,
            WindowsServiceName = EmptyToNull(component.WindowsServiceName),
            ExecutablePath = EmptyToNull(component.ExecutablePath),
            IniPath = EmptyToNull(component.IniPath),
            LogPaths = component.LogPaths?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList(),
            TcpChecks = component.TcpChecks?.Select(check => check is null ? null : new TcpCheckDefinition
            {
                Host = check.Host,
                Port = check.Port,
                TimeoutMs = check.TimeoutMs,
                IsRequired = check.IsRequired
            }).ToList(),
            HttpChecks = component.HttpChecks?.Select(check => check is null ? null : new HttpCheckDefinition
            {
                Url = check.Url,
                Method = check.Method ?? "GET",
                ExpectedStatusMin = check.ExpectedStatusMin,
                ExpectedStatusMax = check.ExpectedStatusMax,
                TimeoutMs = check.TimeoutMs,
                BodyPattern = EmptyToNull(check.BodyPattern),
                ValidateTls = check.ValidateTls,
                CertificateWarningDays = check.CertificateWarningDays,
                IsRequired = check.IsRequired
            }).ToList()
        }).ToList()
    };

    private static Component CreateComponent(SaveComponentRequest request)
    {
        var component = new Component { Name = string.Empty, Type = ComponentType.Generic };
        ApplyComponent(null, component, request);
        return component;
    }

    private static void ApplyComponent(PulseDbContext? dbContext, Component component, SaveComponentRequest request)
    {
        component.Name = request.Name!.Trim();
        component.Type = request.Type!.Value;
        component.IsRequired = request.IsRequired;
        component.IsDemo = false;
        component.Status = HealthStatus.Unknown;
        component.LastStateChangeAt = null;

        Replace(dbContext, component.WindowsServiceTargets);
        Replace(dbContext, component.ProcessTargets);
        Replace(dbContext, component.FileTargets);
        Replace(dbContext, component.TcpChecks);
        Replace(dbContext, component.HttpChecks);

        var serviceName = EmptyToNull(request.WindowsServiceName);
        if (serviceName is not null)
        {
            AddTarget(dbContext, component.WindowsServiceTargets, new WindowsServiceTarget { ServiceName = serviceName });
        }

        var executablePath = EmptyToNull(request.ExecutablePath);
        if (executablePath is not null)
        {
            AddTarget(dbContext, component.ProcessTargets, new ProcessTarget { ExecutablePath = executablePath });
            AddTarget(dbContext, component.FileTargets, new FileTarget { Path = executablePath, Kind = FileTargetKind.Executable });
        }

        var iniPath = EmptyToNull(request.IniPath);
        if (iniPath is not null)
        {
            AddTarget(dbContext, component.FileTargets, new FileTarget { Path = iniPath, Kind = FileTargetKind.Ini });
        }

        SynchronizeLogs(dbContext, component, request.LogPaths ?? []);
        foreach (var check in request.TcpChecks ?? [])
        {
            AddTarget(dbContext, component.TcpChecks, new TcpCheck
            {
                Host = check!.Host!.Trim(),
                Port = check.Port,
                TimeoutMs = check.TimeoutMs,
                IsRequired = check.IsRequired
            });
        }

        foreach (var check in request.HttpChecks ?? [])
        {
            AddTarget(dbContext, component.HttpChecks, new HttpCheck
            {
                Url = check!.Url!.Trim(),
                Method = (check.Method ?? "GET").Trim().ToUpperInvariant(),
                ExpectedStatusMin = check.ExpectedStatusMin,
                ExpectedStatusMax = check.ExpectedStatusMax,
                TimeoutMs = check.TimeoutMs,
                BodyPattern = EmptyToNull(check.BodyPattern),
                ValidateTls = check.ValidateTls,
                CertificateWarningDays = check.CertificateWarningDays,
                IsRequired = check.IsRequired
            });
        }
    }

    private static void SynchronizeLogs(PulseDbContext? dbContext, Component component, IReadOnlyList<string?> requestedPaths)
    {
        var desiredPaths = requestedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var source in component.LogSources
            .Where(source => !desiredPaths.Contains(source.Path, StringComparer.OrdinalIgnoreCase))
            .ToArray())
        {
            dbContext?.LogSources.Remove(source);
            component.LogSources.Remove(source);
        }

        foreach (var path in desiredPaths.Where(path => !component.LogSources.Any(source =>
            string.Equals(source.Path, path, StringComparison.OrdinalIgnoreCase))))
        {
            AddTarget(dbContext, component.LogSources, new LogSource { Path = path });
        }
    }

    private static void AddTarget<TEntity>(PulseDbContext? dbContext, ICollection<TEntity> collection, TEntity entity)
        where TEntity : class
    {
        collection.Add(entity);
        if (dbContext is not null)
        {
            dbContext.Add(entity);
        }
    }

    private static void Replace<TEntity>(PulseDbContext? dbContext, ICollection<TEntity> collection)
        where TEntity : class
    {
        if (dbContext is not null)
        {
            dbContext.RemoveRange(collection);
        }

        collection.Clear();
    }

    private static InstallationConfigurationResponse ToResponse(Installation installation) => new(
        installation.Id,
        installation.Name,
        installation.Environment,
        installation.CustomEnvironmentName,
        DeserializeTags(installation.TagsJson),
        installation.IsDemo,
        installation.Components.OrderBy(item => item.Name).Select(component => new ComponentConfigurationResponse(
            component.Id,
            component.Name,
            component.Type,
            component.IsRequired,
            component.Status,
            component.WindowsServiceTargets.FirstOrDefault()?.ServiceName,
            component.ProcessTargets.FirstOrDefault()?.ExecutablePath,
            component.FileTargets.FirstOrDefault(item => item.Kind == FileTargetKind.Ini)?.Path,
            component.LogSources.OrderBy(item => item.Path).Select(item => item.Path).ToArray(),
            component.TcpChecks.Select(item => new TcpCheckResponse(item.Host, item.Port, item.TimeoutMs, item.IsRequired)).ToArray(),
            component.HttpChecks.Select(item => new HttpCheckResponse(
                item.Url,
                item.Method,
                item.ExpectedStatusMin,
                item.ExpectedStatusMax,
                item.TimeoutMs,
                item.BodyPattern,
                item.ValidateTls,
                item.CertificateWarningDays,
                item.IsRequired)).ToArray())).ToArray());

    private static string SerializeTags(IReadOnlyList<string?>? tags) => JsonSerializer.Serialize((tags ?? [])
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Select(item => item!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string[] DeserializeTags(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddAudit(
        PulseDbContext dbContext,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        DateTimeOffset occurredAt,
        string action,
        Installation installation,
        SaveInstallationRequest request)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = GetUserId(principal),
            Action = action,
            EntityType = nameof(Installation),
            EntityId = installation.Id.ToString(),
            SanitizedDetailsJson = JsonSerializer.Serialize(new
            {
                environment = installation.Environment,
                componentCount = request.Components?.Count ?? 0,
                targetCounts = new
                {
                    services = request.Components?.Count(item => !string.IsNullOrWhiteSpace(item?.WindowsServiceName)) ?? 0,
                    paths = request.Components?.Sum(item => (string.IsNullOrWhiteSpace(item?.ExecutablePath) ? 0 : 1)
                        + (string.IsNullOrWhiteSpace(item?.IniPath) ? 0 : 1)
                        + (item?.LogPaths?.Count ?? 0)) ?? 0,
                    tcp = request.Components?.Sum(item => item?.TcpChecks?.Count ?? 0) ?? 0,
                    http = request.Components?.Sum(item => item?.HttpChecks?.Count ?? 0) ?? 0
                }
            }),
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = occurredAt
        });
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public sealed record SaveInstallationRequest(
        string? Name,
        EnvironmentKind? Environment,
        string? CustomEnvironmentName,
        IReadOnlyList<string?>? Tags,
        IReadOnlyList<SaveComponentRequest?>? Components);

    public sealed record SaveComponentRequest(
        Guid? Id,
        string? Name,
        ComponentType? Type,
        bool IsRequired = true,
        string? WindowsServiceName = null,
        string? ExecutablePath = null,
        string? IniPath = null,
        IReadOnlyList<string?>? LogPaths = null,
        IReadOnlyList<TcpCheckRequest?>? TcpChecks = null,
        IReadOnlyList<HttpCheckRequest?>? HttpChecks = null);

    public sealed record TcpCheckRequest(string? Host, int Port, int TimeoutMs = 3_000, bool IsRequired = true);

    public sealed record HttpCheckRequest(
        string? Url,
        string? Method = "GET",
        int ExpectedStatusMin = 200,
        int ExpectedStatusMax = 399,
        int TimeoutMs = 5_000,
        string? BodyPattern = null,
        bool ValidateTls = true,
        int CertificateWarningDays = 30,
        bool IsRequired = true);

    public sealed record InstallationCreatedResponse(
        Guid Id,
        string Name,
        EnvironmentKind Environment,
        string? CustomEnvironmentName,
        IReadOnlyList<string> Tags,
        int ComponentCount,
        HealthStatus Status);

    public sealed record InstallationConfigurationResponse(
        Guid Id,
        string Name,
        EnvironmentKind Environment,
        string? CustomEnvironmentName,
        IReadOnlyList<string> Tags,
        bool IsDemo,
        IReadOnlyList<ComponentConfigurationResponse> Components);

    public sealed record ComponentConfigurationResponse(
        Guid Id,
        string Name,
        ComponentType Type,
        bool IsRequired,
        HealthStatus Status,
        string? WindowsServiceName,
        string? ExecutablePath,
        string? IniPath,
        IReadOnlyList<string> LogPaths,
        IReadOnlyList<TcpCheckResponse> TcpChecks,
        IReadOnlyList<HttpCheckResponse> HttpChecks);

    public sealed record TcpCheckResponse(string Host, int Port, int TimeoutMs, bool IsRequired);

    public sealed record HttpCheckResponse(
        string Url,
        string Method,
        int ExpectedStatusMin,
        int ExpectedStatusMax,
        int TimeoutMs,
        string? BodyPattern,
        bool ValidateTls,
        int CertificateWarningDays,
        bool IsRequired);
}
