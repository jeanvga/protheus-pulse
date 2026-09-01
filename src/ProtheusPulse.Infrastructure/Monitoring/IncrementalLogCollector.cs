using System.Diagnostics;
using System.Text;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Infrastructure.Monitoring;

public sealed class IncrementalLogCollector(IClock clock, ProbeCollectorOptions options) : IIncrementalLogCollector
{
    private const int MaximumEventsPerCycle = 200;
    private const int MaximumUtf8CharacterBytes = 4;

    private static readonly Encoding LenientUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    private static readonly Encoding Windows1252 = CreateWindows1252();

    public bool CanCollect(Component component) => component.LogSources.Count > 0;

    public async Task<LogCollectionResult> CollectAsync(Component component, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var targetObservations = new List<CollectorSupport.TargetObservation>();
        var events = new List<LogEventObservation>();
        foreach (var source in component.LogSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(source.Path))
            {
                targetObservations.Add(new CollectorSupport.TargetObservation(HealthStatus.Critical, component.IsRequired));
                continue;
            }

            try
            {
                var sourceEvents = await ReadSourceAsync(source, cancellationToken);
                events.AddRange(sourceEvents);
                var status = sourceEvents.Any(item => item.Level == "Critical")
                    ? HealthStatus.Critical
                    : sourceEvents.Any(item => item.Level is "Error" or "Warning")
                        ? HealthStatus.Warning
                        : HealthStatus.Healthy;
                targetObservations.Add(new CollectorSupport.TargetObservation(status, component.IsRequired));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                targetObservations.Add(new CollectorSupport.TargetObservation(HealthStatus.Unknown, component.IsRequired));
            }
        }

        var errorCount = events.Sum(item => item.Level is "Critical" or "Error" ? item.OccurrenceCount : 0);
        IReadOnlyList<MetricObservation> metrics = [new MetricObservation("errors", errorCount, "eventos")];
        var observation = CollectorSupport.CreateObservation(
            stopwatch,
            targetObservations,
            clock.UtcNow,
            events.Count == 0 ? "Nenhum novo evento relevante nos logs." : "Novos eventos informativos coletados dos logs.",
            "Novos avisos ou erros foram encontrados nos logs.",
            "Um evento crítico foi encontrado nos logs.",
            "Não foi possível ler todos os logs configurados.",
            metrics);
        return new LogCollectionResult(observation, events);
    }

    private async Task<IReadOnlyList<LogEventObservation>> ReadSourceAsync(
        LogSource source,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(source.Path);
        var identity = file.CreationTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var maximumBytes = Math.Clamp(options.MaximumLogBytesPerCycle, 4_096, 1_048_576);
        var cursor = source.CursorOffset;
        var skipPartialFirstLine = false;
        if (!string.Equals(source.FileIdentity, identity, StringComparison.Ordinal) || cursor < 0 || cursor > file.Length)
        {
            cursor = Math.Max(0, file.Length - maximumBytes);
            skipPartialFirstLine = cursor > 0;
            source.FileIdentity = identity;
        }

        await using var stream = new FileStream(
            source.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16_384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Seek(cursor, SeekOrigin.Begin);
        var bytesToRead = (int)Math.Min(maximumBytes, Math.Max(0, stream.Length - cursor));
        if (bytesToRead == 0)
        {
            source.CursorOffset = cursor;
            source.LastReadAt = clock.UtcNow;
            return [];
        }

        var buffer = new byte[bytesToRead];
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        var encoding = ResolveEncoding(buffer, totalRead, source.EncodingName);
        var text = encoding.GetString(buffer, 0, totalRead);
        var reachedEnd = cursor + totalRead >= stream.Length;
        var lastNewLine = text.LastIndexOf('\n');
        var completeText = reachedEnd || lastNewLine < 0 ? text : text[..(lastNewLine + 1)];
        var consumedBytes = reachedEnd || lastNewLine < 0 ? totalRead : encoding.GetByteCount(completeText);
        source.CursorOffset = cursor + consumedBytes;
        source.LastReadAt = clock.UtcNow;

        var lines = completeText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var startIndex = skipPartialFirstLine && lines.Length > 0 ? 1 : 0;
        var grouped = new Dictionary<string, MutableLogEvent>(StringComparer.Ordinal);
        if (ProtheusConsoleLog.TryReadRecords(lines, startIndex, out var records))
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (grouped.Count >= MaximumEventsPerCycle)
                {
                    break;
                }

                if (Describe(record) is not { } described)
                {
                    continue;
                }

                Accumulate(grouped, source.Id, ResolveObservedAt(record.Timestamp), described.Level, described.Message);
            }
        }
        else
        {
            // O arquivo não tem o cabeçalho do AppServer: trata linha a linha, como antes.
            for (var index = startIndex; index < lines.Length && grouped.Count < MaximumEventsPerCycle; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = LogTextSanitizer.Sanitize(lines[index]);
                if (line.Length == 0)
                {
                    continue;
                }

                var level = LogTextSanitizer.DetectLevel(line);
                if (level == "Debug")
                {
                    continue;
                }

                Accumulate(grouped, source.Id, clock.UtcNow, level, line);
            }
        }

        return grouped.Values
            .Select(item => new LogEventObservation(
                item.LogSourceId,
                item.ObservedAt,
                item.Level,
                item.Message,
                item.Fingerprint,
                item.Count))
            .ToArray();
    }

    /// <summary>
    /// O <c>console.log</c> do AppServer em Windows pt-BR é gravado em CP1252: lido como
    /// UTF-8, todo acento vira caractere de substituição — e a assinatura da mensagem,
    /// calculada em cima do texto, degrada junto. Com <c>auto</c> o UTF-8 estrito é
    /// tentado primeiro e o CP1252 entra quando ele falha.
    /// </summary>
    private static Encoding ResolveEncoding(byte[] buffer, int count, string name) =>
        name.Trim().ToLowerInvariant() switch
        {
            "unicode" or "utf-16" => Encoding.Unicode,
            "bigendianunicode" or "utf-16be" => Encoding.BigEndianUnicode,
            "ascii" => Encoding.ASCII,
            "utf-8" or "utf8" => LenientUtf8,
            "cp1252" or "windows-1252" or "1252" or "ansi" or "latin1" or "iso-8859-1" => Windows1252,
            _ => LooksLikeUtf8(buffer, count) ? LenientUtf8 : Windows1252
        };

    /// <summary>
    /// Decodifica em UTF-8 estrito para decidir o encoding. Uma falha nos últimos bytes é
    /// ignorada: o bloco lido corta no meio de um caractere multibyte, não é sinal de
    /// outro encoding.
    /// </summary>
    /// <summary>CP1252 não vem no runtime do .NET; o provedor precisa ser registrado.</summary>
    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    private static bool LooksLikeUtf8(byte[] buffer, int count)
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            strict.GetString(buffer, 0, count);
            return true;
        }
        catch (DecoderFallbackException)
        {
            // Sem margem para um caractere partido no fim do bloco.
        }

        if (count <= MaximumUtf8CharacterBytes)
        {
            return false;
        }

        try
        {
            strict.GetString(buffer, 0, count - MaximumUtf8CharacterBytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static void Accumulate(
        Dictionary<string, MutableLogEvent> grouped,
        Guid logSourceId,
        DateTimeOffset observedAt,
        string level,
        string message)
    {
        var fingerprint = LogTextSanitizer.CreateFingerprint(message);
        if (grouped.TryGetValue(fingerprint, out var existing))
        {
            existing.Count++;
            return;
        }

        grouped[fingerprint] = new MutableLogEvent(logSourceId, observedAt, level, message, fingerprint);
    }

    /// <summary>
    /// Reduz um registro a uma linha de evento. Um bloco <c>THREAD ERROR</c> vira a
    /// mensagem do erro com o fonte ADVPL onde ele estourou, e não as milhares de linhas
    /// da pilha; devolve <c>null</c> quando não há nada que valha guardar.
    /// </summary>
    private static (string Level, string Message)? Describe(ConsoleLogRecord record)
    {
        if (ProtheusConsoleLog.TryDescribeThreadError(record.Body) is { } threadError)
        {
            var described = LogTextSanitizer.Sanitize(ProtheusConsoleLog.Describe(threadError));
            var severity = record.Body.Any(line => line.Contains("[FATAL]", StringComparison.OrdinalIgnoreCase))
                ? "Critical"
                : "Error";
            return described.Length == 0 ? null : (severity, described);
        }

        foreach (var candidate in record.Body)
        {
            var message = LogTextSanitizer.Sanitize(candidate);
            if (message.Length == 0)
            {
                continue;
            }

            var level = LogTextSanitizer.DetectLevel(message);
            return level == "Debug" ? null : (level, message);
        }

        return null;
    }

    /// <summary>
    /// O horário vem do próprio AppServer, não da hora em que o Pulse leu o arquivo. Um
    /// valor fora de faixa — relógio errado no servidor ou linha corrompida — cai de volta
    /// no relógio do Pulse.
    /// </summary>
    private DateTimeOffset ResolveObservedAt(DateTimeOffset candidate)
    {
        var now = clock.UtcNow;
        return candidate > now.AddHours(1) || candidate < now.AddDays(-7)
            ? now
            : candidate.ToUniversalTime();
    }

    private sealed class MutableLogEvent(
        Guid logSourceId,
        DateTimeOffset observedAt,
        string level,
        string message,
        string fingerprint)
    {
        public Guid LogSourceId { get; } = logSourceId;
        public DateTimeOffset ObservedAt { get; } = observedAt;
        public string Level { get; } = level;
        public string Message { get; } = message;
        public string Fingerprint { get; } = fingerprint;
        public int Count { get; set; } = 1;
    }
}
