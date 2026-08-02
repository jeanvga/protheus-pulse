using System.Diagnostics;
using System.Text;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Infrastructure.Monitoring;

public sealed class IncrementalLogCollector(IClock clock, ProbeCollectorOptions options) : IIncrementalLogCollector
{
    private const int MaximumEventsPerCycle = 200;

    // Origens alimentadas por agente chegam prontas pela API; ler o mesmo arquivo
    // aqui duplicaria cada evento.
    public bool CanCollect(Component component) => component.LogSources.Any(item => !item.IsAgentManaged);

    public async Task<LogCollectionResult> CollectAsync(Component component, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var targetObservations = new List<CollectorSupport.TargetObservation>();
        var events = new List<LogEventObservation>();
        foreach (var source in component.LogSources.Where(item => !item.IsAgentManaged))
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

        var encoding = ResolveEncoding(source.EncodingName);
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

            var fingerprint = LogTextSanitizer.CreateFingerprint(line);
            if (grouped.TryGetValue(fingerprint, out var existing))
            {
                existing.Count++;
            }
            else
            {
                grouped[fingerprint] = new MutableLogEvent(source.Id, clock.UtcNow, level, line, fingerprint);
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

    private static Encoding ResolveEncoding(string name) => name.Trim().ToLowerInvariant() switch
    {
        "unicode" or "utf-16" => Encoding.Unicode,
        "bigendianunicode" or "utf-16be" => Encoding.BigEndianUnicode,
        "ascii" => Encoding.ASCII,
        _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
    };

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
