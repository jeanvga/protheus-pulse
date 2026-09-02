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

    public IReadOnlyList<string> Validate() =>
        Port is < MinimumPort or > MaximumPort
            ? [$"Network:Port deve estar entre {MinimumPort} e {MaximumPort}."]
            : [];

    public string BuildUrl() => $"http://{(AllowRemoteAccess ? "0.0.0.0" : "127.0.0.1")}:{Port}";

    /// <summary>
    /// Sobrescritas de configuração que o host precisa aplicar. São duas, e faltar
    /// qualquer uma deixa o acesso remoto sem efeito: um endpoint declarado em
    /// <c>Kestrel:Endpoints</c> tem precedência sobre <c>UseUrls</c>, e o filtro de host
    /// recusa a requisição cujo <c>Host</c> é o IP do servidor — que é exatamente o que o
    /// operador digita ao abrir de outra máquina.
    /// </summary>
    public Dictionary<string, string?> BuildOverrides()
    {
        var overrides = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Kestrel:Endpoints:Http:Url"] = BuildUrl()
        };
        if (AllowRemoteAccess)
        {
            overrides["AllowedHosts"] = "*";
        }

        return overrides;
    }
}

/// <summary>Caminho de <c>C:\ProgramData\ProtheusPulse</c> resolvido na inicialização.</summary>
public sealed record PulseDataDirectory(string Path);
