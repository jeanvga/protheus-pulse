using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtheusPulse.Infrastructure.Persistence;

namespace ProtheusPulse.IntegrationTests;

public sealed class PulseApiTests : IClassFixture<PulseWebApplicationFactory>
{
    private static readonly string[] PilotTags = ["piloto", "servidor-a"];
    private readonly HttpClient client;
    private readonly PulseWebApplicationFactory factory;

    public PulseApiTests(PulseWebApplicationFactory factory)
    {
        this.factory = factory;
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

    [Fact]
    public async Task AdministratorCanCreateInstallationAndAuditUsesSanitizedDetails()
    {
        var installationName = $"ERP Piloto {Guid.NewGuid():N}";
        var token = await AuthenticateDemoAdministratorAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/installations", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                name = installationName,
                environment = "Production",
                tags = PilotTags,
                components = new List<ComponentPayload>
                {
                    new("AppServer REST", "AppServer"),
                    new("License Server", "LicenseServer")
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<InstallationResponse>();
        Assert.NotNull(created);
        Assert.Equal(installationName, created.Name);
        Assert.Equal("Production", created.Environment);
        Assert.Equal(2, created.ComponentCount);
        Assert.Equal("Unknown", created.Status);
        Assert.Equal($"/api/v1/installations/{created.Id}", response.Headers.Location?.OriginalString);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var audit = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(item => item.EntityId == created.Id.ToString());
        Assert.Equal("InstallationCreated", audit.Action);
        Assert.NotNull(audit.UserId);
        Assert.Contains("\"componentCount\":2", audit.SanitizedDetailsJson, StringComparison.Ordinal);
        Assert.DoesNotContain(installationName, audit.SanitizedDetailsJson, StringComparison.Ordinal);

        using var duplicateRequest = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/installations", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                name = installationName.ToLowerInvariant(),
                environment = "Production",
                components = new List<ComponentPayload> { new("AppServer", "AppServer") }
            })
        };
        duplicateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var duplicateResponse = await client.SendAsync(duplicateRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task InstallationCreationValidatesMalformedComponents()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/installations", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                name = $"Ambiente inválido {Guid.NewGuid():N}",
                environment = "Homologation",
                components = new List<ComponentPayload>
                {
                    new("AppServer", "AppServer"),
                    new("appserver", "Worker")
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("nomes dos componentes devem ser únicos", content, StringComparison.OrdinalIgnoreCase);

        using var nullComponentRequest = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/installations", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                name = $"Ambiente nulo {Guid.NewGuid():N}",
                environment = "Homologation",
                components = new List<object?> { null }
            })
        };
        nullComponentRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var nullComponentResponse = await client.SendAsync(nullComponentRequest);
        Assert.Equal(HttpStatusCode.BadRequest, nullComponentResponse.StatusCode);
        var nullComponentContent = await nullComponentResponse.Content.ReadAsStringAsync();
        Assert.Contains("Informe os dados do componente", nullComponentContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallationCreationRejectsAnonymousRequests()
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/installations", UriKind.Relative), new
        {
            name = "Ambiente sem autorização",
            environment = "Development",
            components = new List<ComponentPayload> { new("AppServer", "AppServer") }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> AuthenticateDemoAdministratorAsync()
    {
        var authStatus = await client.GetFromJsonAsync<AuthStatusResponse>(
            new Uri("/api/v1/auth/status", UriKind.Relative));
        Assert.NotNull(authStatus);

        var login = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new
        {
            username = authStatus.DemoUsername,
            password = authStatus.DemoPassword
        });
        login.EnsureSuccessStatusCode();
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);
        return token.AccessToken;
    }

    private sealed record TokenResponse(string AccessToken);
    private sealed record AuthStatusResponse(string DemoUsername, string DemoPassword);
    private sealed record ComponentPayload(string Name, string Type, bool IsRequired = true);
    private sealed record InstallationResponse(Guid Id, string Name, string Environment, int ComponentCount, string Status);
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
