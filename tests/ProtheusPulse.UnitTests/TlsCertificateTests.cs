using ProtheusPulse.Service.Configuration;

namespace ProtheusPulse.UnitTests;

/// <summary>
/// O painel oferece acesso pela rede e recomendava HTTPS sem ter como configurá-lo.
/// O certificado gerado aqui precisa servir de fato: com chave privada e aprovado
/// pela mesma conferência que a gravação faz antes de aceitar ligar o TLS.
/// </summary>
public sealed class TlsCertificateTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"pulse-tls-{Guid.NewGuid():N}");

    public TlsCertificateTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OCertificadoGeradoPassaNaPropriaConferencia()
    {
        var created = TlsCertificates.CreateSelfSigned(directory, password: null);

        Assert.True(File.Exists(created.Path));
        Assert.Contains(Environment.MachineName, created.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.True(created.NotAfter > DateTime.UtcNow.AddYears(1));

        var check = TlsCertificates.Inspect(created.Path, password: null);
        Assert.True(check.Valid, check.Message);
    }

    [Fact]
    public void ArquivoInexistenteEhRecusadoComMensagemUtil()
    {
        var check = TlsCertificates.Inspect(Path.Combine(directory, "nao-existe.pfx"), password: null);

        Assert.False(check.Valid);
        Assert.Contains("não foi encontrado", check.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void ArquivoQueNaoEhCertificadoEhRecusado()
    {
        var path = Path.Combine(directory, "qualquer-coisa.pfx");
        File.WriteAllText(path, "isto não é um certificado");

        var check = TlsCertificates.Inspect(path, password: null);

        Assert.False(check.Valid);
        Assert.NotNull(check.Message);
    }
}
