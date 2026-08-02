using ProtheusPulse.Application.Abstractions;

namespace ProtheusPulse.Service.Endpoints;

public static class ServerEndpoints
{
    public static RouteGroupBuilder MapServerResources(this RouteGroupBuilder api)
    {
        api.MapGet("/server/resources", (IServerResourceMonitor monitor, ServerResourceOptions options) =>
            Results.Ok(new ServerResourcesResponse(
                monitor.GetSnapshot(),
                new ServerThresholds(
                    options.CpuWarningPercent,
                    options.CpuCriticalPercent,
                    options.MemoryWarningPercent,
                    options.MemoryCriticalPercent,
                    options.DiskWarningPercent,
                    options.DiskCriticalPercent))))
            .RequireAuthorization("Viewer");
        return api;
    }

    public sealed record ServerResourcesResponse(ServerResourceSnapshot Server, ServerThresholds Thresholds);

    /// <summary>
    /// Limites usados para colorir a aba Servidor. CPU e memória são percentuais
    /// de <em>uso</em>; disco é percentual <em>livre</em>, como no restante do Pulse.
    /// </summary>
    public sealed record ServerThresholds(
        double CpuWarningPercent,
        double CpuCriticalPercent,
        double MemoryWarningPercent,
        double MemoryCriticalPercent,
        double DiskFreeWarningPercent,
        double DiskFreeCriticalPercent);
}
