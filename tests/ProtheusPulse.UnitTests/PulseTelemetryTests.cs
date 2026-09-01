using System.Diagnostics.Metrics;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Service.Observability;

namespace ProtheusPulse.UnitTests;

public sealed class PulseTelemetryTests
{
    [Fact]
    public void RecordProbePublishesDurationAndCurrentResultWithBoundedTags()
    {
        using var capture = new MetricCapture();
        using var telemetry = new PulseTelemetry();

        telemetry.RecordProbe(
            "Matriz",
            "AppServer REST",
            ProbeType.Http,
            HealthStatus.Healthy,
            required: true,
            TimeSpan.FromMilliseconds(1_250));
        capture.RecordObservableInstruments();

        var duration = capture.Single("protheus.pulse.probe.duration");
        Assert.Equal(1.25, Assert.IsType<double>(duration.Value), precision: 3);
        Assert.Equal("Matriz", duration.Tags["installation"]);
        Assert.Equal("AppServer REST", duration.Tags["component"]);
        Assert.Equal("http", duration.Tags["probe.type"]);
        Assert.Equal(true, duration.Tags["required"]);
        Assert.Equal("healthy", duration.Tags["status"]);

        var result = capture.Single("protheus.pulse.probe.up");
        Assert.Equal(1, Assert.IsType<int>(result.Value));
        Assert.Equal(
            ["component", "installation", "probe.type", "required", "status"],
            result.Tags.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(result.Tags.Keys, key => key.Contains("message", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Tags.Keys, key => key.Contains("evidence", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Tags.Keys, key => key.Contains("path", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(HealthStatus.Unknown, 0)]
    [InlineData(HealthStatus.Healthy, 1)]
    [InlineData(HealthStatus.Warning, 2)]
    [InlineData(HealthStatus.Critical, 3)]
    [InlineData(HealthStatus.Maintenance, 4)]
    public void RecordComponentHealthPublishesStableStateMapping(HealthStatus status, int expected)
    {
        using var capture = new MetricCapture();
        using var telemetry = new PulseTelemetry();

        telemetry.RecordComponentHealth("Matriz", "AppServer", status);
        capture.RecordObservableInstruments();

        var measurement = capture.Single("protheus.pulse.component.health");
        Assert.Equal(expected, Assert.IsType<int>(measurement.Value));
        Assert.Equal(status.ToString().ToLowerInvariant(), measurement.Tags["status"]);
        Assert.Equal(status == HealthStatus.Maintenance, measurement.Tags["maintenance"]);
    }

    [Fact]
    public void RecordCollectionCyclePublishesOutcomeDurationAndProcessedComponents()
    {
        using var capture = new MetricCapture();
        using var telemetry = new PulseTelemetry();

        telemetry.RecordCollectionCycle(success: false, processedComponents: 3, TimeSpan.FromSeconds(2.5));

        var cycle = capture.Single("protheus.pulse.collection.cycles");
        Assert.Equal(1L, Assert.IsType<long>(cycle.Value));
        Assert.Equal("failure", cycle.Tags["outcome"]);
        Assert.Equal(2.5, Assert.IsType<double>(capture.Single("protheus.pulse.collection.duration").Value), precision: 3);
        Assert.Equal(3L, Assert.IsType<long>(capture.Single("protheus.pulse.collection.components").Value));
    }

    [Fact]
    public void RecordLogEventUsesOnlyNormalizedLevelAndCount()
    {
        using var capture = new MetricCapture();
        using var telemetry = new PulseTelemetry();

        telemetry.RecordLogEvent("Matriz", "AppServer", "customer-specific-value", 7);

        var measurement = capture.Single("protheus.pulse.log.events");
        Assert.Equal(7L, Assert.IsType<long>(measurement.Value));
        Assert.Equal("unknown", measurement.Tags["level"]);
        Assert.Equal(["component", "installation", "level"], measurement.Tags.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly List<CapturedMeasurement> measurements = [];
        private readonly MeterListener listener = new();

        public MetricCapture()
        {
            listener.InstrumentPublished = (instrument, currentListener) =>
            {
                if (instrument.Meter.Name == PulseTelemetry.MeterName)
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Capture(instrument, value, tags));
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Capture(instrument, value, tags));
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Capture(instrument, value, tags));
            listener.Start();
        }

        public CapturedMeasurement Single(string instrumentName) =>
            Assert.Single(measurements, item => item.Name == instrumentName);

        public void RecordObservableInstruments() => listener.RecordObservableInstruments();

        public void Dispose() => listener.Dispose();

        private void Capture<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            where T : struct =>
            measurements.Add(new CapturedMeasurement(
                instrument.Name,
                value,
                tags.ToArray().ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)));
    }

    private sealed record CapturedMeasurement(
        string Name,
        object Value,
        IReadOnlyDictionary<string, object?> Tags);
}
