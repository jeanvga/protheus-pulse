namespace ProtheusPulse.Service.Configuration;

/// <summary>Onde o serviço escuta o painel.</summary>
public sealed class NetworkOptions
{
    public const string SectionName = "Network";
    public const int MinimumPort = 1_024;
    public const int MaximumPort = 65_535;

    /// <summary>
    /// Falso mantém o painel em <c>127.0.0.1</c>, acessível apenas do próprio servidor.
    /// Verdadeiro escuta em todas as interfaces, para abrir de outra máquina por
    /// <c>http://ip:porta</c> — sem TLS, então a rede precisa ser confiável ou o acesso
    /// deve passar por um proxy HTTPS.
    /// </summary>
    public bool AllowRemoteAccess { get; set; }

    public int Port { get; set; } = 5058;

    /// <summary>
    /// Serve o painel por TLS. Sem isso a senha e o token de sessão trafegam em texto
    /// claro, o que só é aceitável no loopback — e o acesso pela rede é justamente o que
    /// tira o tráfego do loopback.
    /// </summary>
    public bool UseHttps { get; set; }

    /// <summary>Caminho do arquivo PFX com a chave privada do certificado.</summary>
    public string? CertificatePath { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Port is < MinimumPort or > MaximumPort)
        {
            errors.Add($"Network:Port deve estar entre {MinimumPort} e {MaximumPort}.");
        }

        if (UseHttps && string.IsNullOrWhiteSpace(CertificatePath))
        {
            errors.Add("Network:CertificatePath é obrigatório quando o HTTPS está ligado.");
        }

        return errors;
    }

    public string Scheme => UseHttps ? "https" : "http";

    public string BuildUrl() => $"{Scheme}://{(AllowRemoteAccess ? "0.0.0.0" : "127.0.0.1")}:{Port}";

    /// <summary>
    /// Sobrescritas de configuração que o host precisa aplicar. São duas, e faltar
    /// qualquer uma deixa o acesso remoto sem efeito: um endpoint declarado em
    /// <c>Kestrel:Endpoints</c> tem precedência sobre <c>UseUrls</c>, e o filtro de host
    /// recusa a requisição cujo <c>Host</c> é o IP do servidor — que é exatamente o que o
    /// operador digita ao abrir de outra máquina.
    /// </summary>
    /// <param name="certificatePassword">
    /// Senha do PFX, já decifrada. Vai só para a configuração em memória: em disco ela
    /// fica protegida com Data Protection, fora do <c>network.json</c>.
    /// </param>
    public Dictionary<string, string?> BuildOverrides(string? certificatePassword = null)
    {
        // Declarar os dois endpoints deixaria HTTP e HTTPS disputando a mesma porta.
        var endpoint = UseHttps ? "Https" : "Http";
        var overrides = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"Kestrel:Endpoints:{endpoint}:Url"] = BuildUrl()
        };
        if (UseHttps)
        {
            overrides["Kestrel:Endpoints:Https:Certificate:Path"] = CertificatePath;
            if (!string.IsNullOrEmpty(certificatePassword))
            {
                overrides["Kestrel:Endpoints:Https:Certificate:Password"] = certificatePassword;
            }
        }

        if (AllowRemoteAccess)
        {
            overrides["AllowedHosts"] = "*";
        }

        return overrides;
    }
}

/// <summary>Caminho de <c>C:\ProgramData\ProtheusPulse</c> resolvido na inicialização.</summary>
public sealed record PulseDataDirectory(string Path);
