namespace ProtheusPulse.Service.Configuration;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool Enabled { get; set; }
    public string OtlpEndpoint { get; set; } = "http://127.0.0.1:4318";
    public string ServiceNamespace { get; set; } = "protheus";
    public int ExportIntervalSeconds { get; set; } = 10;

    public IReadOnlyList<string> Validate()
    {
        if (!Enabled)
        {
            return [];
        }

        var errors = new List<string>();
        if (!TryValidateEndpoint(OtlpEndpoint, out var endpointError))
        {
            errors.Add(endpointError);
        }

        if (!IsValidServiceNamespace(ServiceNamespace))
        {
            errors.Add("Observability:ServiceNamespace deve ter de 1 a 64 caracteres alfanuméricos, ponto, hífen ou sublinhado.");
        }

        if (ExportIntervalSeconds is < 1 or > 300)
        {
            errors.Add("Observability:ExportIntervalSeconds deve estar entre 1 e 300.");
        }

        return errors;
    }

    private static bool TryValidateEndpoint(string value, out string error)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(endpoint.Host))
        {
            error = "Observability:OtlpEndpoint deve ser uma URI absoluta HTTP ou HTTPS.";
            return false;
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            error = "Observability:OtlpEndpoint não pode conter credenciais, query string ou fragmento.";
            return false;
        }

        if (endpoint.Scheme == Uri.UriSchemeHttp && !endpoint.IsLoopback)
        {
            error = "Observability:OtlpEndpoint permite HTTP somente em loopback; use HTTPS fora da máquina local.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsValidServiceNamespace(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !char.IsAsciiLetterOrDigit(value[0]))
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }
}
