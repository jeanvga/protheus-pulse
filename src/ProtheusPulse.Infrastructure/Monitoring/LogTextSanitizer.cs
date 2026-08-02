using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ProtheusPulse.Infrastructure.Monitoring;

/// <summary>
/// Regras de saneamento das linhas de log, compartilhadas entre a leitura
/// incremental feita pelo próprio Pulse e a ingestão vinda de agentes externos.
/// O que chega de fora passa pelas mesmas regras: o agente nunca é fonte confiável.
/// </summary>
public static partial class LogTextSanitizer
{
    public const int MaximumMessageLength = 1_000;
    private const int MaximumLineCharacters = 4_096;
    private const string Redacted = "$1=[REDACTED]";

    /// <summary>Remove controles, corta o excesso e mascara segredos conhecidos.</summary>
    public static string Sanitize(string line)
    {
        var bounded = line.Length <= MaximumLineCharacters ? line : line[..MaximumLineCharacters];
        var clean = new string(bounded.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
        // O Bearer vem primeiro: em "Authorization: Bearer xyz" a regra de atribuição
        // consumiria apenas a palavra "Bearer" e deixaria o token à mostra.
        clean = BearerTokenRegex().Replace(clean, "Bearer [REDACTED]");
        clean = SensitiveAssignmentRegex().Replace(clean, Redacted);
        return clean.Length <= MaximumMessageLength ? clean : clean[..MaximumMessageLength];
    }

    public static string DetectLevel(string line)
    {
        if (line.Contains("fatal", StringComparison.OrdinalIgnoreCase)
            || line.Contains("critical", StringComparison.OrdinalIgnoreCase))
        {
            return "Critical";
        }

        if (line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (line.Contains("warn", StringComparison.OrdinalIgnoreCase))
        {
            return "Warning";
        }

        if (line.Contains("debug", StringComparison.OrdinalIgnoreCase)
            || line.Contains("trace", StringComparison.OrdinalIgnoreCase))
        {
            return "Debug";
        }

        return "Information";
    }

    /// <summary>
    /// Assinatura estável da mensagem: números viram <c>#</c> para que a mesma falha
    /// com identificadores diferentes seja agrupada em um evento só.
    /// </summary>
    public static string CreateFingerprint(string line)
    {
        var normalized = new string(line.ToLowerInvariant().Select(character => char.IsDigit(character) ? '#' : character).ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    [GeneratedRegex(
        "(?i)(password|passwd|pwd|secret|token|credential|authorization|privatekey|cryptkey|accesskey|apikey|clientsecret)\\s*[:=]\\s*[\\\"']?[^,;\\s\\\"']+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SensitiveAssignmentRegex();

    [GeneratedRegex(
        "(?i)Bearer\\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex BearerTokenRegex();
}
