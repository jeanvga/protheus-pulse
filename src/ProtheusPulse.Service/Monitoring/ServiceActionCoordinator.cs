using System.Collections.Concurrent;

namespace ProtheusPulse.Service.Monitoring;

/// <summary>
/// Registro em memória dos serviços com uma ação do painel em execução agora.
/// O watchdog do auto-start consulta este registro para não competir com um
/// restart em andamento: sem isso, religar durante a fase de parada faria a
/// espera pelo estado esperado falhar e reportar um erro que não existe.
/// </summary>
public sealed class ServiceActionCoordinator
{
    private readonly ConcurrentDictionary<string, byte> inFlight = new(StringComparer.OrdinalIgnoreCase);

    public bool IsBusy(string serviceName) => inFlight.ContainsKey(serviceName);

    /// <summary>Marca o serviço como ocupado até o descarte do escopo retornado.</summary>
    public IDisposable BeginAction(string serviceName)
    {
        inFlight[serviceName] = 0;
        return new ActionScope(this, serviceName);
    }

    private void EndAction(string serviceName) => inFlight.TryRemove(serviceName, out _);

    private sealed class ActionScope(ServiceActionCoordinator coordinator, string serviceName) : IDisposable
    {
        public void Dispose() => coordinator.EndAction(serviceName);
    }
}
