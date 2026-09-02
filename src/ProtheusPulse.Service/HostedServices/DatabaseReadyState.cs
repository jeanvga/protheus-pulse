namespace ProtheusPulse.Service.HostedServices;

/// <summary>
/// Sinaliza quando o banco terminou de migrar. O SCM exige que o serviço se registre em
/// 30 segundos, e a migração de um banco já grande passa disso — por isso ela sai do
/// caminho de inicialização e a prontidão é publicada aqui, para o health check e para
/// os workers que só podem consultar depois do esquema aplicado.
/// </summary>
public sealed class DatabaseReadyState
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsReady { get; private set; }

    /// <summary>Mensagem da falha de migração, quando houve uma.</summary>
    public string? Failure { get; private set; }

    public void MarkReady()
    {
        IsReady = true;
        ready.TrySetResult();
    }

    public void MarkFailed(string failure) => Failure = failure;

    /// <summary>Espera o esquema ficar pronto; sai antes se o serviço estiver parando.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (IsReady)
        {
            return;
        }

        var cancellation = new TaskCompletionSource();
        await using var registration = cancellationToken.Register(() => cancellation.TrySetResult());
        await Task.WhenAny(ready.Task, cancellation.Task);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
