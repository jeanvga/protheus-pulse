using System.Globalization;
using System.Text.RegularExpressions;

namespace ProtheusPulse.Infrastructure.Monitoring;

/// <summary>Um registro do <c>console.log</c>: o cabeçalho e as linhas que vieram depois dele.</summary>
public sealed record ConsoleLogRecord(DateTimeOffset Timestamp, string ThreadId, IReadOnlyList<string> Body);

/// <summary>Identificação estruturada de um bloco <c>THREAD ERROR</c> do AppServer.</summary>
public sealed record ThreadErrorSummary(string User, string Computer, string Message, string? SourceFile, int? SourceLine);

/// <summary>
/// Leitura do <c>console.log</c> do AppServer no formato que o Protheus realmente escreve.
/// Cada registro começa por uma linha de cabeçalho
/// <c>2026-01-15T09:12:33.400000-03:00 4321|</c> e continua nas linhas seguintes até o
/// próximo cabeçalho — a mensagem, a pilha ADVPL e o rodapé do erro fazem parte do mesmo
/// evento. Ler linha a linha quebraria um erro em dezenas de registros soltos e perderia
/// o horário que o próprio AppServer gravou.
/// </summary>
public static partial class ProtheusConsoleLog
{
    /// <summary>Teto de linhas guardadas por registro; um bloco de erro passa de dez mil.</summary>
    public const int MaximumBodyLines = 400;

    private const int MinimumHeaderLength = 26;

    /// <summary>Reconhece a linha de cabeçalho e devolve horário, thread e o que sobrou dela.</summary>
    public static bool TryParseHeader(
        string line,
        out DateTimeOffset timestamp,
        out string threadId,
        out string remainder)
    {
        timestamp = default;
        threadId = string.Empty;
        remainder = string.Empty;
        if (line.Length < MinimumHeaderLength || line[4] != '-' || line[7] != '-' || line[10] != 'T')
        {
            return false;
        }

        var space = line.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0)
        {
            return false;
        }

        var pipe = line.IndexOf('|', space + 1);
        if (pipe < 0)
        {
            return false;
        }

        var thread = line.AsSpan(space + 1, pipe - space - 1);
        if (thread.Length == 0)
        {
            return false;
        }

        foreach (var character in thread)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        if (!DateTimeOffset.TryParse(
                line.AsSpan(0, space),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out timestamp))
        {
            return false;
        }

        threadId = thread.ToString();
        remainder = line[(pipe + 1)..];
        return true;
    }

    /// <summary>
    /// Agrupa as linhas em registros. Devolve <c>false</c> quando nenhum cabeçalho é
    /// encontrado: o arquivo não é um <c>console.log</c> do AppServer e o chamador deve
    /// continuar tratando linha a linha.
    /// </summary>
    public static bool TryReadRecords(
        IReadOnlyList<string> lines,
        int startIndex,
        out IReadOnlyList<ConsoleLogRecord> records)
    {
        var found = new List<ConsoleLogRecord>();
        DateTimeOffset timestamp = default;
        var threadId = string.Empty;
        List<string>? body = null;
        for (var index = startIndex; index < lines.Count; index++)
        {
            if (TryParseHeader(lines[index], out var headerTimestamp, out var headerThread, out var remainder))
            {
                if (body is not null)
                {
                    found.Add(new ConsoleLogRecord(timestamp, threadId, body));
                }

                timestamp = headerTimestamp;
                threadId = headerThread;
                body = [];
                if (remainder.Length > 0)
                {
                    body.Add(remainder);
                }

                continue;
            }

            // Linhas antes do primeiro cabeçalho continuam um registro que já foi lido
            // em um ciclo anterior; reprocessá-las duplicaria o evento.
            if (body is not null && body.Count < MaximumBodyLines)
            {
                body.Add(lines[index]);
            }
        }

        if (body is not null)
        {
            found.Add(new ConsoleLogRecord(timestamp, threadId, body));
        }

        records = found;
        return found.Count > 0;
    }

    /// <summary>
    /// Descreve um bloco <c>THREAD ERROR</c>: quem executava, qual foi o erro e em que
    /// fonte ADVPL ele estourou. É o que transforma um despejo de milhares de linhas em
    /// uma mensagem que cabe em um e-mail.
    /// </summary>
    public static ThreadErrorSummary? TryDescribeThreadError(IReadOnlyList<string> body)
    {
        for (var index = 0; index < body.Count; index++)
        {
            var match = ThreadErrorHeaderRegex().Match(body[index]);
            if (!match.Success)
            {
                continue;
            }

            var message = string.Empty;
            for (var next = index + 1; next < body.Count; next++)
            {
                var candidate = body[next].Trim();
                if (candidate.Length > 0)
                {
                    message = candidate;
                    break;
                }
            }

            string? sourceFile = null;
            int? sourceLine = null;
            foreach (var line in body)
            {
                var origin = SourceOriginRegex().Match(line);
                if (!origin.Success)
                {
                    continue;
                }

                sourceFile = origin.Groups["file"].Value;
                sourceLine = int.TryParse(origin.Groups["line"].Value, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
                break;
            }

            return new ThreadErrorSummary(
                match.Groups["user"].Value.Trim(),
                match.Groups["computer"].Value.Trim(),
                message,
                sourceFile,
                sourceLine);
        }

        return null;
    }

    /// <summary>Monta a mensagem curta do bloco de erro, já com origem no fonte ADVPL.</summary>
    public static string Describe(ThreadErrorSummary summary)
    {
        var who = summary.Computer.Length > 0
            ? $"{summary.User}@{summary.Computer}"
            : summary.User;
        var text = $"THREAD ERROR {who}: {summary.Message}";
        if (summary.SourceFile is not null && !summary.Message.Contains("line :", StringComparison.OrdinalIgnoreCase))
        {
            text = summary.SourceLine is null
                ? $"{text} em {summary.SourceFile}"
                : $"{text} em {summary.SourceFile}:{summary.SourceLine}";
        }

        return text;
    }

    [GeneratedRegex(
        @"^THREAD ERROR \(\[\d+\],\s*(?<user>[^,]*),\s*(?<computer>[^)]*)\)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex ThreadErrorHeaderRegex();

    [GeneratedRegex(
        @"\((?<file>[A-Za-z0-9_.\-]+\.(?i:PRW|PRX|TLPP|APH|APL|CH))\)[^|]*?line\s*:\s*(?<line>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SourceOriginRegex();
}
