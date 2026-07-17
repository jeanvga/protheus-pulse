namespace ProtheusPulse.Service.Configuration;

public static class SanitizedIniReader
{
    public const long MaximumFileBytes = 512 * 1024;
    private const int MaximumEntries = 2_000;
    private const int MaximumLineLength = 8_192;
    private const string RedactedValue = "[REDACTED]";

    private static readonly string[] SensitiveFragments =
    [
        "password", "passwd", "pwd", "secret", "token", "credential", "authorization",
        "privatekey", "cryptkey", "accesskey", "apikey", "clientsecret"
    ];

    public static async Task<IniReadResult> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (file.Length > MaximumFileBytes)
        {
            return IniReadResult.Failure($"O INI deve possuir no máximo {MaximumFileBytes / 1024} KiB.");
        }

        var entries = new List<IniEntry>();
        var section = string.Empty;
        var redactedCount = 0;
        var lineNumber = 0;
        await foreach (var rawLine in File.ReadLinesAsync(path, cancellationToken))
        {
            lineNumber++;
            if (lineNumber > 10_000)
            {
                return IniReadResult.Failure("O INI excede o limite de 10000 linhas.");
            }

            if (rawLine.Length > MaximumLineLength)
            {
                return IniReadResult.Failure($"A linha {lineNumber} excede o limite permitido.");
            }

            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = Sanitize(line[1..^1], 80);
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (entries.Count >= MaximumEntries)
            {
                return IniReadResult.Failure($"O INI excede o limite de {MaximumEntries} propriedades.");
            }

            var key = Sanitize(line[..separator], 120);
            if (key.Length == 0)
            {
                continue;
            }

            var redacted = IsSensitive(key);
            var value = redacted ? RedactedValue : Sanitize(line[(separator + 1)..], 500);
            if (redacted)
            {
                redactedCount++;
            }

            entries.Add(new IniEntry(section, key, value, redacted));
        }

        return IniReadResult.Success(entries, redactedCount);
    }

    private static bool IsSensitive(string key)
    {
        var normalized = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return SensitiveFragments.Any(normalized.Contains);
    }

    private static string Sanitize(string value, int maximumLength)
    {
        var sanitized = new string(value.Trim().Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength];
    }

    public sealed record IniEntry(string Section, string Key, string Value, bool Redacted);

    public sealed record IniReadResult(
        bool Valid,
        IReadOnlyList<IniEntry> Entries,
        int RedactedCount,
        IReadOnlyList<string> Errors)
    {
        public static IniReadResult Success(IReadOnlyList<IniEntry> entries, int redactedCount) =>
            new(true, entries, redactedCount, []);

        public static IniReadResult Failure(string error) => new(false, [], 0, [error]);
    }
}
