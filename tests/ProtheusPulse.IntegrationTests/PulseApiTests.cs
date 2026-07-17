using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProtheusPulse.IntegrationTests;

public sealed class PulseApiTests : IClassFixture<PulseWebApplicationFactory>
{
    private readonly HttpClient client;

    public PulseApiTests(PulseWebApplicationFactory factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task LivenessIsPublicAndHealthy()
    {
        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DashboardRejectsAnonymousRequests()
    {
        var response = await client.GetAsync(new Uri("/api/v1/dashboard/summary", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DemoUserCanAuthenticateAndReadDashboard()
    {
        var login = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new
        {
            username = "demo.admin",
            password = "PulseDemo!2026"
        });
        login.EnsureSuccessStatusCode();
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/dashboard/summary", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("ERP Produção", content, StringComparison.Ordinal);
        Assert.Contains("\"demoMode\":true", content, StringComparison.Ordinal);
    }

    private sealed record TokenResponse(string AccessToken);
}

public sealed class PulseWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "protheus-pulse-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Pulse:DemoMode", "true");
        builder.UseSetting("Pulse:DataDirectory", dataDirectory);
    }
}
