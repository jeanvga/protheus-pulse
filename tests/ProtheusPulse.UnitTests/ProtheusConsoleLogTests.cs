using System.Text;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Monitoring;

namespace ProtheusPulse.UnitTests;

/// <summary>
/// Leitura do <c>console.log</c> do AppServer. Os textos abaixo reproduzem o formato real
/// com dados sintéticos: nenhum nome de cliente, usuário ou ambiente verdadeiro entra aqui.
/// </summary>
public sealed class ProtheusConsoleLogTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 7, 17, 22, 0, 0, TimeSpan.Zero));

    [Fact]
    public void HeaderCarriesTimestampThreadAndRemainder()
    {
        Assert.True(ProtheusConsoleLog.TryParseHeader(
            "2026-07-17T18:55:11.097000-03:00 9684|Thread ADD:213360004",
            out var timestamp,
            out var threadId,
            out var remainder));

        Assert.Equal(new DateTimeOffset(2026, 7, 17, 18, 55, 11, 97, TimeSpan.FromHours(-3)), timestamp);
        Assert.Equal("9684", threadId);
        Assert.Equal("Thread ADD:213360004", remainder);
    }

    [Theory]
    [InlineData("[INFO ][SERVER] [Thread 9684] INACTIVETIMEOUT changed")]
    [InlineData("Called from U_MEUFONTE(MEUFONTE.PRW) 01/01/2026 08:00:00 line : 122")]
    [InlineData("2026-07-17T18:55:11.097000-03:00 sem-pipe")]
    [InlineData("")]
    public void LinesWithoutTheAppServerHeaderAreNotRecognized(string line)
    {
        Assert.False(ProtheusConsoleLog.TryParseHeader(line, out _, out _, out _));
    }

    [Fact]
    public void ContinuationLinesStayInTheSameRecord()
    {
        string[] lines =
        [
            "2026-07-17T18:55:11.097000-03:00 9684|",
            "[INFO ][SERVER] Starting Program MDIExecute Thread 9684 (deposito01,P0001234)",
            "(d:\\protheus\\apo\\custom.rpo)",
            "2026-07-17T18:55:12.500000-03:00 9684|",
            "[WARN ][SERVER] DefferredDelete Active Object"
        ];

        Assert.True(ProtheusConsoleLog.TryReadRecords(lines, 0, out var records));

        Assert.Equal(2, records.Count);
        Assert.Equal(2, records[0].Body.Count);
        Assert.Equal("9684", records[0].ThreadId);
        Assert.Single(records[1].Body);
    }

    [Fact]
    public void LinesBeforeTheFirstHeaderBelongToAnAlreadyReportedRecord()
    {
        string[] lines =
        [
            "Called from U_MEUFONTE(MEUFONTE.PRW) 01/01/2026 08:00:00 line : 122",
            "[dbthread: 13104]",
            "2026-07-17T18:55:11.097000-03:00 9684|",
            "[INFO ][SERVER] pronto"
        ];

        Assert.True(ProtheusConsoleLog.TryReadRecords(lines, 0, out var records));

        var record = Assert.Single(records);
        Assert.Equal("[INFO ][SERVER] pronto", Assert.Single(record.Body));
    }

    [Fact]
    public void AFileWithoutHeadersFallsBackToLineMode()
    {
        string[] lines = ["INFO pronto", "ERROR falhou"];

        Assert.False(ProtheusConsoleLog.TryReadRecords(lines, 0, out var records));
        Assert.Empty(records);
    }

    [Fact]
    public void ThreadErrorBlockYieldsUserComputerMessageAndAdvplSource()
    {
        string[] body =
        [
            "/*-------------------------------------------------------",
            "THREAD ERROR ([332], usuario.teste, P0001234)   17/07/2026   18:55:11",
            "Connection terminated by the administrator. on GETFONTSIZE(REPORT01.PRW) 01/01/2026 08:00:00 line : 4497",
            "",
            "[environment: AMBIENTEP]",
            "Called from U_MEUFONTE(MEUFONTE.PRW) 01/01/2026 08:00:00 line : 122"
        ];

        var summary = ProtheusConsoleLog.TryDescribeThreadError(body);

        Assert.NotNull(summary);
        Assert.Equal("usuario.teste", summary.User);
        Assert.Equal("P0001234", summary.Computer);
        Assert.Equal("REPORT01.PRW", summary.SourceFile);
        Assert.Equal(4497, summary.SourceLine);
        Assert.StartsWith("Connection terminated by the administrator.", summary.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeAppendsTheAdvplSourceWhenTheMessageDoesNotCarryIt()
    {
        var summary = new ThreadErrorSummary("usuario.teste", "P0001234", "DKD010: DB error (Update)", "APLIB060.PRW", 183);

        Assert.Equal(
            "THREAD ERROR usuario.teste@P0001234: DKD010: DB error (Update) em APLIB060.PRW:183",
            ProtheusConsoleLog.Describe(summary));
    }

    [Fact]
    public void RecordsWithoutAThreadErrorAreNotDescribedAsOne()
    {
        string[] body = ["[INFO ][SERVER] [Thread 9684] INACTIVETIMEOUT changed from [600] to [0]"];

        Assert.Null(ProtheusConsoleLog.TryDescribeThreadError(body));
    }

    [Fact]
    public async Task CollectorKeepsAccentsOfACp1252ConsoleLog()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var content = string.Join(
            "\r\n",
            "2026-07-17T18:55:11.097000-03:00 9684|",
            "[ERROR][SERVER] Falha na conexão com o serviço de emissão",
            string.Empty);

        var events = await CollectAsync(content, Encoding.GetEncoding(1252));

        var single = Assert.Single(events);
        Assert.Equal("Error", single.Level);
        Assert.Contains("conexão", single.Message, StringComparison.Ordinal);
        Assert.Contains("emissão", single.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', single.Message);
    }

    [Fact]
    public async Task CollectorUsesTheTimestampWrittenByTheAppServer()
    {
        var content = string.Join(
            "\r\n",
            "2026-07-17T18:55:11.097000-03:00 9684|",
            "[ERROR][SERVER] ReceiveDataPack - Status not reading - 5",
            string.Empty);

        var events = await CollectAsync(content, Encoding.UTF8);

        var single = Assert.Single(events);
        Assert.Equal(new DateTimeOffset(2026, 7, 17, 21, 55, 11, 97, TimeSpan.Zero), single.ObservedAt);
        Assert.NotEqual(Clock.UtcNow, single.ObservedAt);
    }

    [Fact]
    public async Task AThreadErrorBecomesOneEventInsteadOfOnePerStackLine()
    {
        var stack = string.Join(
            "\r\n",
            Enumerable.Range(1, 40).Select(index =>
                $"Called from U_FONTE{index}(FONTE{index}.PRW) 01/01/2026 08:00:00 line : {index}"));
        var content = string.Join(
            "\r\n",
            "2026-07-17T18:55:11.097000-03:00 332|",
            "/*-------------------------------------------------------",
            "THREAD ERROR ([332], usuario.teste, P0001234)   17/07/2026   18:55:11",
            "Connection terminated by the administrator. on GETFONTSIZE(REPORT01.PRW) 01/01/2026 08:00:00 line : 4497",
            "[environment: AMBIENTEP]",
            stack,
            string.Empty);

        var events = await CollectAsync(content, Encoding.UTF8);

        var single = Assert.Single(events);
        Assert.Equal("Error", single.Level);
        Assert.Contains("THREAD ERROR usuario.teste@P0001234", single.Message, StringComparison.Ordinal);
        Assert.Contains("Connection terminated by the administrator.", single.Message, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<LogEventObservation>> CollectAsync(string content, Encoding encoding)
    {
        var root = Path.Combine(Path.GetTempPath(), "pulse-console-log", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "console.log");
        await File.WriteAllBytesAsync(path, encoding.GetBytes(content));
        try
        {
            var component = new Component
            {
                InstallationId = Guid.NewGuid(),
                Name = "AppServer sintético",
                Type = ComponentType.Generic,
                IsRequired = true
            };
            component.LogSources.Add(new LogSource { Path = path });
            var collector = new IncrementalLogCollector(Clock, new ProbeCollectorOptions());
            var result = await collector.CollectAsync(component, CancellationToken.None);
            return result.Events;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
