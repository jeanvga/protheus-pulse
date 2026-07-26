namespace ProtheusPulse.Domain.Monitoring;

/// <summary>
/// Regras de leitura do estado de um serviço Windows. Os nomes seguem os valores
/// de <c>System.ServiceProcess.ServiceControllerStatus</c> para que a mesma
/// avaliação valha na API, no watchdog e no painel.
/// </summary>
public static class ServiceStateRules
{
    public const string Running = "Running";
    public const string Stopped = "Stopped";

    private static readonly string[] KnownStates =
    [
        Running, Stopped, "Paused", "StartPending", "StopPending", "PausePending", "ContinuePending"
    ];

    /// <summary>Estado reconhecido do SCM, e não uma falha da ação ("Timeout", "Error").</summary>
    public static bool IsKnown(string? status) =>
        status is not null && KnownStates.Contains(status, StringComparer.OrdinalIgnoreCase);

    public static bool IsRunning(string? status) =>
        string.Equals(status, Running, StringComparison.OrdinalIgnoreCase);

    public static bool IsStopped(string? status) =>
        string.Equals(status, Stopped, StringComparison.OrdinalIgnoreCase);

    public static bool IsTransitioning(string? status) => status is not null && (
        status.Equals("StartPending", StringComparison.OrdinalIgnoreCase)
        || status.Equals("StopPending", StringComparison.OrdinalIgnoreCase)
        || status.Equals("ContinuePending", StringComparison.OrdinalIgnoreCase)
        || status.Equals("PausePending", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Indica se a ação faz sentido para o estado atual. Um estado desconhecido
    /// libera todas as ações, porque o operador precisa conseguir intervir.
    /// </summary>
    public static bool AllowsAction(string? status, string action) => action switch
    {
        "start" => !IsRunning(status) && !IsTransitioning(status),
        "stop" => !IsStopped(status) && !IsTransitioning(status),
        "restart" => !IsStopped(status) && !IsTransitioning(status),
        _ => false
    };
}

/// <summary>Serviço monitorado com a origem que o operador enxerga no painel.</summary>
public sealed record MonitoredService(string ServiceName, bool BelongsToExclusiveInstallation);

/// <summary>Serviços que devem parar e serviços que devem subir ao entrar em manutenção.</summary>
public sealed record MaintenanceServicePlan(
    IReadOnlyList<string> ToStop,
    IReadOnlyList<string> ToStart);

public static class MaintenancePlanner
{
    /// <summary>
    /// Monta o plano do modo manutenção: a instalação exclusiva sobe e todo o
    /// restante para. Um serviço compartilhado com a instalação exclusiva nunca
    /// entra na lista de parada, senão a manutenção derrubaria o próprio ambiente
    /// reservado para compilar e salvar configurações.
    /// </summary>
    public static MaintenanceServicePlan Create(IEnumerable<MonitoredService> services)
    {
        var exclusiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var otherNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in services)
        {
            if (string.IsNullOrWhiteSpace(service.ServiceName))
            {
                continue;
            }

            var target = service.BelongsToExclusiveInstallation ? exclusiveNames : otherNames;
            target.Add(service.ServiceName.Trim());
        }

        otherNames.ExceptWith(exclusiveNames);
        return new MaintenanceServicePlan(
            otherNames.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
            exclusiveNames.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

/// <summary>Tentativas de religar um serviço dentro da janela corrente.</summary>
public sealed record AutoStartAttempt(int Count, DateTimeOffset FirstAttemptAt);

/// <summary>
/// Política do auto-start: religa um serviço parado, mas com orçamento de
/// tentativas para não entrar em laço quando o ambiente não sobe de verdade.
/// </summary>
public static class AutoStartPolicy
{
    public const int MaximumAttempts = 3;
    public static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

    public static bool ShouldAttempt(string? serviceStatus, AutoStartAttempt? attempt, DateTimeOffset now)
    {
        if (!ServiceStateRules.IsStopped(serviceStatus))
        {
            return false;
        }

        return attempt is null
            || now - attempt.FirstAttemptAt > AttemptWindow
            || attempt.Count < MaximumAttempts;
    }

    public static AutoStartAttempt Register(AutoStartAttempt? attempt, DateTimeOffset now) =>
        attempt is null || now - attempt.FirstAttemptAt > AttemptWindow
            ? new AutoStartAttempt(1, now)
            : attempt with { Count = attempt.Count + 1 };
}
