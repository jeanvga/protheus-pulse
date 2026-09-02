using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using System.ServiceProcess;
using System.Text.Json;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Configuration;

namespace ProtheusPulse.Service.Endpoints;

public static class DiscoveryEndpoints
{
    private const int MaximumRoots = 5;
    private const int MaximumNames = 20;

    public static RouteGroupBuilder MapDiscovery(this RouteGroupBuilder api)
    {
        api.MapGet("/discovery/services", DiscoverServicesAsync).RequireAuthorization("Administrator");
        api.MapPost("/discovery/paths", DiscoverPathsAsync).RequireAuthorization("Administrator");
        api.MapPost("/discovery/ini", InspectIniAsync).RequireAuthorization("Administrator");
        api.MapPost("/discovery/component", ProposeComponentAsync).RequireAuthorization("Administrator");
        return api;
    }

    /// <summary>
    /// Aponta uma pasta e o Pulse classifica sozinho o que achou: executável, INI, logs e
    /// as portas declaradas no INI. Antes era preciso localizar arquivo por arquivo e dizer
    /// manualmente o que cada um era.
    /// </summary>
    private static async Task<IResult> ProposeComponentAsync(
        ComponentProposalRequest request,
        CancellationToken cancellationToken)
    {
        if (!SafeFileAccess.TryResolveRoot(request.Root, out var root, out var rootError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["root"] = [rootError] });
        }

        var executables = new List<string>();
        var inis = new List<string>();
        var logs = new List<string>();
        var inspected = 0;
        foreach (var file in EnumerateBounded(root, MaximumProposalDepth))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++inspected > MaximumProposalFiles)
            {
                break;
            }

            var name = Path.GetFileName(file);
            var extension = Path.GetExtension(file);
            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("appserver", StringComparison.OrdinalIgnoreCase))
                {
                    executables.Add(file);
                }
            }
            else if (extension.Equals(".ini", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("appserver", StringComparison.OrdinalIgnoreCase))
                {
                    inis.Add(file);
                }
            }
            else if (extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
                && name.Contains("console", StringComparison.OrdinalIgnoreCase))
            {
                logs.Add(file);
            }
        }

        var ports = new List<int>();
        var iniPath = inis.OrderBy(item => item.Length).FirstOrDefault();
        if (iniPath is not null)
        {
            var ini = await SanitizedIniReader.ReadAsync(iniPath, cancellationToken);
            if (ini.Valid)
            {
                foreach (var entry in ini.Entries)
                {
                    if (entry.Key.Contains("port", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(entry.Value, CultureInfo.InvariantCulture, out var port)
                        && port is > 0 and <= 65_535
                        && !ports.Contains(port))
                    {
                        ports.Add(port);
                    }
                }
            }
        }

        return Results.Ok(new ComponentProposal(
            Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)),
            executables.OrderBy(item => item.Length).FirstOrDefault(),
            iniPath,
            logs.OrderBy(item => item.Length).Take(MaximumProposalLogs).ToArray(),
            ports.Take(MaximumProposalPorts).ToArray(),
            inspected));
    }

    /// <summary>Varredura limitada em profundidade, ignorando pasta sem permissão.</summary>
    private static IEnumerable<string> EnumerateBounded(string root, int depth)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            yield return file;
        }

        if (depth <= 0)
        {
            yield break;
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            foreach (var file in EnumerateBounded(directory, depth - 1))
            {
                yield return file;
            }
        }
    }

    private static async Task<IResult> DiscoverServicesAsync(
        string? nameContains,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(nameContains) || nameContains.Trim().Length < 2 || nameContains.Length > 80)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["nameContains"] = ["Informe ao menos dois caracteres para limitar a descoberta."]
            });
        }

        if (limit is < 1 or > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["O limite deve estar entre 1 e 200."]
            });
        }

        if (!OperatingSystem.IsWindows())
        {
            return Results.Ok(new { supported = false, dryRun = true, candidates = Array.Empty<object>() });
        }

        var filter = nameContains.Trim();
        var candidates = new List<ServiceCandidate>();
        foreach (var service in ServiceController.GetServices())
        {
            using (service)
            {
                if (!service.ServiceName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !service.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(new ServiceCandidate(service.ServiceName, service.DisplayName, service.Status.ToString()));
                if (candidates.Count >= limit)
                {
                    break;
                }
            }
        }

        await AddAuditAsync(dbContext, clock, principal, httpContext, "WindowsServicesDiscovered", candidates.Count, cancellationToken);
        return Results.Ok(new { supported = true, dryRun = true, candidates });
    }

    private static async Task<IResult> DiscoverPathsAsync(
        PathDiscoveryRequest request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidatePathRequest(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var roots = new List<string>();
        foreach (var requestedRoot in request.Roots!)
        {
            if (!SafeFileAccess.TryResolveRoot(requestedRoot, out var root, out var error))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["roots"] = [error] });
            }

            roots.Add(root);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();
        var candidates = new List<PathCandidate>();
        var timedOut = false;
        try
        {
            await Task.Run(
                () => ScanRoots(roots, request.FileNames!, request.MaxDepth, request.MaxResults, candidates, timeout.Token),
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
        }

        stopwatch.Stop();
        await AddAuditAsync(dbContext, clock, principal, httpContext, "PathsDiscovered", candidates.Count, cancellationToken);
        return Results.Ok(new
        {
            dryRun = true,
            timedOut,
            durationMs = stopwatch.ElapsedMilliseconds,
            candidates
        });
    }

    private static async Task<IResult> InspectIniAsync(
        IniInspectionRequest request,
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!SafeFileAccess.TryResolveExistingFile(request.Root, request.Path, ".ini", out var path, out var error))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["path"] = [error] });
        }

        var result = await SanitizedIniReader.ReadAsync(path, cancellationToken);
        if (!result.Valid)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["path"] = result.Errors.ToArray() });
        }

        await AddAuditAsync(dbContext, clock, principal, httpContext, "IniInspected", result.Entries.Count, cancellationToken);
        return Results.Ok(new
        {
            dryRun = true,
            result.Entries,
            result.RedactedCount,
            note = "Comentários e valores sensíveis não são retornados."
        });
    }

    private static Dictionary<string, string[]> ValidatePathRequest(PathDiscoveryRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.Roots is null || request.Roots.Count == 0 || request.Roots.Count > MaximumRoots)
        {
            errors["roots"] = [$"Informe de 1 a {MaximumRoots} raízes explícitas."];
        }

        if (request.FileNames is null || request.FileNames.Count == 0 || request.FileNames.Count > MaximumNames
            || request.FileNames.Any(name => string.IsNullOrWhiteSpace(name)
                || name.Length > 120
                || name.Any(char.IsControl)
                || name.IndexOfAny(['*', '?', '/', '\\']) >= 0
                || !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal)))
        {
            errors["fileNames"] = [$"Informe de 1 a {MaximumNames} nomes exatos, sem curingas ou diretórios."];
        }

        if (request.MaxDepth is < 0 or > 8)
        {
            errors["maxDepth"] = ["A profundidade deve estar entre 0 e 8."];
        }

        if (request.MaxResults is < 1 or > 200)
        {
            errors["maxResults"] = ["O limite de resultados deve estar entre 1 e 200."];
        }

        if (request.TimeoutSeconds is < 1 or > 15)
        {
            errors["timeoutSeconds"] = ["O timeout deve estar entre 1 e 15 segundos."];
        }

        return errors;
    }

    private static void ScanRoots(
        IReadOnlyList<string> roots,
        IReadOnlyList<string> fileNames,
        int maximumDepth,
        int maximumResults,
        List<PathCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var names = fileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((root, 0));
            while (pending.Count > 0 && candidates.Count < maximumResults)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();
                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(current.Path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (current.Depth < maximumDepth)
                        {
                            pending.Push((entry, current.Depth + 1));
                        }

                        continue;
                    }

                    if (names.Contains(Path.GetFileName(entry)))
                    {
                        candidates.Add(new PathCandidate(entry, Path.GetFileName(entry)));
                        if (candidates.Count >= maximumResults)
                        {
                            break;
                        }
                    }
                }
            }
        }
    }

    private static async Task AddAuditAsync(
        PulseDbContext dbContext,
        IClock clock,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        string action,
        int resultCount,
        CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            UserId = GetUserId(principal),
            Action = action,
            EntityType = "Discovery",
            SanitizedDetailsJson = JsonSerializer.Serialize(new { resultCount, dryRun = true }),
            RemoteAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private const int MaximumProposalDepth = 3;
    private const int MaximumProposalFiles = 6_000;
    private const int MaximumProposalLogs = 5;
    private const int MaximumProposalPorts = 6;

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public sealed record PathDiscoveryRequest(
        IReadOnlyList<string>? Roots,
        IReadOnlyList<string>? FileNames,
        int MaxDepth = 4,
        int MaxResults = 100,
        int TimeoutSeconds = 10);

    public sealed record IniInspectionRequest(string? Root, string? Path);

    public sealed record ComponentProposalRequest(string? Root);

    public sealed record ComponentProposal(
        string SuggestedName,
        string? ExecutablePath,
        string? IniPath,
        IReadOnlyList<string> LogPaths,
        IReadOnlyList<int> Ports,
        int FilesInspected);
    public sealed record ServiceCandidate(string ServiceName, string DisplayName, string Status);
    public sealed record PathCandidate(string Path, string FileName);
}
