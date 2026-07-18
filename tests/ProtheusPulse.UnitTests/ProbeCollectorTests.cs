using System.Net;
using System.Net.Sockets;
using System.Text;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Monitoring;

namespace ProtheusPulse.UnitTests;

public sealed class ProbeCollectorTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 7, 17, 22, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task FileCollectorReportsExistingAndMissingRequiredTargets()
    {
        var root = Path.Combine(Path.GetTempPath(), "pulse-file-collector", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "appserver.ini");
        await File.WriteAllTextAsync(path, "[Environment]");
        try
        {
            var component = CreateComponent();
            component.FileTargets.Add(new FileTarget { Path = path, Kind = FileTargetKind.Ini });
            var collector = new FileProbeCollector(Clock);

            var healthy = await collector.CollectAsync(component, CancellationToken.None);
            Assert.Equal(HealthStatus.Healthy, healthy.Status);

            File.Delete(path);
            var critical = await collector.CollectAsync(component, CancellationToken.None);
            Assert.Equal(HealthStatus.Critical, critical.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TcpCollectorUsesResolvedAddressAndRecordsLatency()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var serverTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var accepted = listener.AcceptTcpClientAsync(serverTimeout.Token);
            var component = CreateComponent();
            component.TcpChecks.Add(new TcpCheck
            {
                Host = IPAddress.Loopback.ToString(),
                Port = endpoint.Port,
                TimeoutMs = 2_000
            });
            var collector = new TcpProbeCollector(Clock);

            var observation = await collector.CollectAsync(component, CancellationToken.None);
            using var connection = await accepted;

            Assert.Equal(HealthStatus.Healthy, observation.Status);
            Assert.Contains(observation.Metrics ?? [], item => item.Name == "latency");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task HttpCollectorDoesNotNeedRedirectAndChecksBoundedBody()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var serverTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var server = ServeSingleHttpResponseAsync(
                listener,
                "HTTP/1.1 200 OK\r\nContent-Length: 11\r\nConnection: close\r\n\r\nPULSE_READY",
                serverTimeout.Token);
            var component = CreateComponent();
            component.HttpChecks.Add(new HttpCheck
            {
                Url = $"http://127.0.0.1:{endpoint.Port}/health",
                ExpectedStatusMax = 299,
                BodyPattern = "PULSE_READY",
                TimeoutMs = 2_000
            });
            using var collector = new HttpProbeCollector(Clock);

            var observation = await collector.CollectAsync(component, CancellationToken.None);
            await server;

            Assert.Equal(HealthStatus.Healthy, observation.Status);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task IncrementalLogCollectorRedactsAndDoesNotReadSameBytesTwice()
    {
        var root = Path.Combine(Path.GetTempPath(), "pulse-log-collector", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "console.log");
        const string fixtureValue = "synthetic-sensitive-value";
        await File.WriteAllTextAsync(path, $"INFO ready\nERROR Password={fixtureValue} request 123 failed\nERROR Password={fixtureValue} request 456 failed\n");
        try
        {
            var component = CreateComponent();
            component.LogSources.Add(new LogSource { Path = path });
            var collector = new IncrementalLogCollector(Clock, new ProbeCollectorOptions());

            var first = await collector.CollectAsync(component, CancellationToken.None);
            var second = await collector.CollectAsync(component, CancellationToken.None);

            Assert.Equal(HealthStatus.Warning, first.Observation.Status);
            var error = Assert.Single(first.Events, item => item.Level == "Error");
            Assert.Equal(2, error.OccurrenceCount);
            Assert.Contains("[REDACTED]", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(fixtureValue, error.Message, StringComparison.Ordinal);
            Assert.Empty(second.Events);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NetworkConnectorBlocksMetadataAndUnspecifiedAddresses()
    {
        Assert.False(SafeNetworkConnector.IsAllowed(IPAddress.Parse("169.254.169.254")));
        Assert.False(SafeNetworkConnector.IsAllowed(IPAddress.Parse("::ffff:169.254.169.254")));
        Assert.False(SafeNetworkConnector.IsAllowed(IPAddress.Any));
        Assert.True(SafeNetworkConnector.IsAllowed(IPAddress.Loopback));
        Assert.True(SafeNetworkConnector.IsAllowed(IPAddress.Parse("10.0.0.10")));
    }

    [Fact]
    public async Task HeartbeatCollectorReportsDelayWithoutExposingDefinitionDetails()
    {
        var component = CreateComponent();
        component.HeartbeatDefinitions.Add(new HeartbeatDefinition
        {
            Name = "Job sintético",
            JobKey = "job_synthetic_test",
            TokenHash = new string('A', 64),
            ExpectedIntervalSeconds = 300,
            ToleranceSeconds = 60,
            LastHeartbeatAt = Clock.UtcNow.AddMinutes(-20)
        });
        var collector = new HeartbeatProbeCollector(Clock);

        var observation = await collector.CollectAsync(component, CancellationToken.None);

        Assert.Equal(HealthStatus.Critical, observation.Status);
        Assert.Contains(observation.Metrics ?? [], item => item.Name == "heartbeatDelay" && item.Value == 20);
        Assert.DoesNotContain("job_synthetic_test", observation.SanitizedEvidence, StringComparison.Ordinal);
        Assert.DoesNotContain("Job sintético", observation.SanitizedEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HeartbeatCollectorDoesNotAlertOutsideConfiguredWindow()
    {
        var localTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(Clock.UtcNow, TimeZoneInfo.Local).DateTime);
        var component = CreateComponent();
        component.HeartbeatDefinitions.Add(new HeartbeatDefinition
        {
            Name = "Job com janela",
            JobKey = "job_window_test",
            ExpectedIntervalSeconds = 60,
            ToleranceSeconds = 0,
            LastHeartbeatAt = Clock.UtcNow.AddDays(-1),
            WindowStart = localTime.AddHours(1),
            WindowEnd = localTime.AddHours(2)
        });
        var collector = new HeartbeatProbeCollector(Clock);

        var observation = await collector.CollectAsync(component, CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, observation.Status);
        Assert.Null(observation.Metrics);
    }

    private static Component CreateComponent() => new()
    {
        InstallationId = Guid.NewGuid(),
        Name = "Componente sintético",
        Type = ComponentType.Generic,
        IsRequired = true
    };

    private static async Task ServeSingleHttpResponseAsync(
        TcpListener listener,
        string response,
        CancellationToken cancellationToken)
    {
        using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = connection.GetStream();
        var requestBuffer = new byte[4_096];
        _ = await stream.ReadAsync(requestBuffer, cancellationToken);
        var responseBytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(responseBytes, cancellationToken);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
