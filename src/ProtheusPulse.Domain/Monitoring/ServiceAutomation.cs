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

/// <summary>
/// Serviços que devem parar e serviços da instalação exclusiva, que são reiniciados.
/// Reiniciar — em vez de apenas manter no ar — derruba as sessões já conectadas,
/// que é o que torna o ambiente realmente exclusivo durante a manutenção.
/// </summary>
public sealed record MaintenanceServicePlan(
    IReadOnlyList<string> ToStop,
    IReadOnlyList<string> ToRestart);

public static class MaintenancePlanner
{
    /// <summary>
    /// Monta o plano do modo manutenção: a instalação exclusiva é reiniciada e todo
    /// o restante para. Um serviço compartilhado com a instalação exclusiva nunca
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

/// <summary>
/// Estado de recuperação de um serviço, guardado por alvo monitorado.
/// <paramref name="Suspended"/> significa que o watchdog não toca mais no serviço
/// até um start pelo painel — seja porque alguém o parou de propósito
/// (<paramref name="FailureCount"/> zero), seja porque as tentativas se esgotaram.
/// </summary>
public sealed record AutoStartState(int FailureCount, DateTimeOffset? RetryAfter, bool Suspended)
{
    public static AutoStartState Clean { get; } = new(0, null, false);
}

/// <summary>
/// Política do auto-start: religa um serviço parado espaçando as tentativas e
/// desistindo quando o ambiente simplesmente não sobe. Sem isso, um serviço que
/// falha por configuração ou licença ficaria sendo iniciado indefinidamente,
/// enchendo a auditoria e o log de eventos do Windows a cada ciclo.
/// </summary>
public static class AutoStartPolicy
{
    /// <summary>Falhas consecutivas antes de o watchdog desistir e exigir intervenção.</summary>
    public const int MaximumFailures = 5;

    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(30);

    /// <param name="actionInFlight">
    /// Há uma ação do painel executando neste serviço agora. Religar no meio de um
    /// restart competiria com a própria ação e faria a espera pelo estado falhar.
    /// </param>
    public static bool ShouldAttempt(
        string? serviceStatus,
        AutoStartState state,
        DateTimeOffset now,
        bool actionInFlight = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Suspended || actionInFlight || !ServiceStateRules.IsStopped(serviceStatus))
        {
            return false;
        }

        return state.RetryAfter is null || now >= state.RetryAfter.Value;
    }

    /// <summary>Espaça a próxima tentativa e desiste ao esgotar o orçamento de falhas.</summary>
    public static AutoStartState RegisterFailure(AutoStartState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        var failures = state.FailureCount + 1;
        return new AutoStartState(failures, now + BackoffFor(failures), failures >= MaximumFailures);
    }

    public static AutoStartState RegisterSuccess() => AutoStartState.Clean;

    /// <summary>Espera dobrada a cada falha, limitada a <see cref="MaximumRetryDelay"/>.</summary>
    public static TimeSpan BackoffFor(int failureCount)
    {
        if (failureCount <= 1)
        {
            return FirstRetryDelay;
        }

        var ticks = FirstRetryDelay.Ticks * Math.Pow(2, Math.Min(failureCount - 1, 16));
        return ticks >= MaximumRetryDelay.Ticks ? MaximumRetryDelay : TimeSpan.FromTicks((long)ticks);
    }
}
