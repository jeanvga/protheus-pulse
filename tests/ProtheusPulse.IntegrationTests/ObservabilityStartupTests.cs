using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProtheusPulse.Service.Configuration;
using ProtheusPulse.Service.Observability;

namespace ProtheusPulse.IntegrationTests;

[Collection("Pulse web application")]
public sealed class ObservabilityStartupTests
{
    [Fact]
    public async Task DisabledExporterStartsWithoutExternalDependency()
    {
        await using var factory = new ObservabilityWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Observability:Enabled"] = "false",
            ["Observability:OtlpEndpoint"] = "not-used-while-disabled"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(factory.Services.GetRequiredService<ObservabilityOptions>().Enabled);
        Assert.NotNull(factory.Services.GetRequiredService<PulseTelemetry>());
    }

    [Fact]
    public void InsecureRemoteHttpEndpointStopsStartup()
    {
        using var factory = new ObservabilityWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Observability:Enabled"] = "true",
            ["Observability:OtlpEndpoint"] = "http://10.0.0.20:4318"
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("loopback", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnabledLoopbackExporterDoesNotBlockLivenessWhenCollectorIsOffline()
    {
        await using var factory = new ObservabilityWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Observability:Enabled"] = "true",
            ["Observability:OtlpEndpoint"] = "http://127.0.0.1:4317",
            ["Observability:ExportIntervalSeconds"] = "1"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(factory.Services.GetRequiredService<ObservabilityOptions>().Enabled);
    }

    [Fact]
    public async Task EnabledExporterPostsToMetricsSignalPathAtConfiguredInterval()
    {
        await using var receiver = await OtlpTestReceiver.StartAsync();
        await using var factory = new ObservabilityWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Observability:Enabled"] = "true",
            ["Observability:OtlpEndpoint"] = receiver.Address,
            ["Observability:ExportIntervalSeconds"] = "1"
        });
        using var client = factory.CreateClient();

        _ = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        var receivedPath = await receiver.ReceivedPath.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("/v1/metrics", receivedPath);
    }

    private sealed class ObservabilityWebApplicationFactory(IReadOnlyDictionary<string, string?> settings)
        : WebApplicationFactory<Program>
    {
        private readonly string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "protheus-pulse-observability-tests",
            Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Pulse:DemoMode", "true");
            builder.UseSetting("Pulse:DataDirectory", dataDirectory);
            builder.UseSetting("Pulse:DiskWarningPercent", "1");
            builder.UseSetting("Pulse:DiskCriticalPercent", "0");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }

    private sealed class OtlpTestReceiver(WebApplication application, TaskCompletionSource<string> receivedPath)
        : IAsyncDisposable
    {
        public string Address => Assert.Single(application.Urls);
        public Task<string> ReceivedPath => receivedPath.Task;

        public static async Task<OtlpTestReceiver> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Configuration.Sources.Clear();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var application = builder.Build();
            var receivedPath = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            application.MapPost("/{**path}", context =>
            {
                receivedPath.TrySetResult(context.Request.Path.Value ?? string.Empty);
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            });
            await application.StartAsync();
            return new OtlpTestReceiver(application, receivedPath);
        }

        public async ValueTask DisposeAsync()
        {
            await application.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
