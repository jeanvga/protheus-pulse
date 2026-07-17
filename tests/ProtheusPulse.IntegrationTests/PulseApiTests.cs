using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.HostedServices;

namespace ProtheusPulse.IntegrationTests;

public sealed class PulseApiTests : IClassFixture<PulseWebApplicationFactory>
{
    private static readonly string[] IniFileNames = ["appserver.ini"];
    private static readonly string[] PilotTags = ["piloto", "servidor-a"];
    private static readonly string[] SampleWindowsRoots = ["C:\\TOTVS"];
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

    [Fact]
    public async Task YamlImportRequiresPreviewAndPersistsConfiguredTargets()
    {
        var installationName = $"ERP Importado {Guid.NewGuid():N}";
        var yaml = $$"""
            schemaVersion: 1
            installations:
              - name: {{installationName}}
                environment: production
                tags:
                  - piloto
                components:
                  - name: AppServer REST
                    type: appserver
                    windowsServiceName: PulsePilotAppServer
                    executablePath: 'D:\PulsePilot\appserver.exe'
                    iniPath: 'D:\PulsePilot\appserver.ini'
                    logPaths:
                      - 'D:\PulsePilot\logs\console.log'
                    tcpChecks:
                      - host: 127.0.0.1
                        port: 18080
                    httpChecks:
                      - url: 'http://127.0.0.1:18080/health'
                        expectedStatusMax: 299
            """;
        var token = await AuthenticateDemoAdministratorAsync();

        using var previewRequest = AuthorizedPost("/api/v1/installations/import/preview", token, new
        {
            format = "yaml",
            content = yaml
        });
        var previewResponse = await client.SendAsync(previewRequest);
        previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"valid\":true", preview, StringComparison.Ordinal);
        Assert.Contains("\"componentCount\":1", preview, StringComparison.Ordinal);

        using var unconfirmedRequest = AuthorizedPost("/api/v1/installations/import", token, new
        {
            format = "yaml",
            content = yaml,
            confirm = false
        });
        var unconfirmedResponse = await client.SendAsync(unconfirmedRequest);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmedResponse.StatusCode);

        using var importRequest = AuthorizedPost("/api/v1/installations/import", token, new
        {
            format = "yaml",
            content = yaml,
            confirm = true
        });
        var importResponse = await client.SendAsync(importRequest);
        Assert.Equal(HttpStatusCode.Created, importResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var installation = await dbContext.Installations
            .AsNoTracking()
            .Include(item => item.Components)
            .SingleAsync(item => item.Name == installationName);
        var componentId = Assert.Single(installation.Components).Id;
        Assert.Equal(1, await dbContext.WindowsServiceTargets.CountAsync(item => item.ComponentId == componentId));
        Assert.Equal(1, await dbContext.ProcessTargets.CountAsync(item => item.ComponentId == componentId));
        Assert.Equal(1, await dbContext.TcpChecks.CountAsync(item => item.ComponentId == componentId));
        Assert.Equal(1, await dbContext.HttpChecks.CountAsync(item => item.ComponentId == componentId));
        Assert.Equal(1, await dbContext.LogSources.CountAsync(item => item.ComponentId == componentId));

        var audit = await dbContext.AuditEvents
            .AsNoTracking()
            .OrderByDescending(item => item.OccurredAt)
            .FirstAsync(item => item.Action == "InstallationsImported");
        Assert.DoesNotContain(installationName, audit.SanitizedDetailsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("D:\\PulsePilot", audit.SanitizedDetailsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportPreviewRejectsUnknownFieldsWithoutEchoingTheirValues()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        const string marker = "sensitive-value-must-not-return";
        var content = $$"""
            {
              "schemaVersion": 1,
              "unexpectedSecret": "{{marker}}",
              "installations": []
            }
            """;
        using var request = AuthorizedPost("/api/v1/installations/import/preview", token, new
        {
            format = "json",
            content
        });

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"valid\":false", responseContent, StringComparison.Ordinal);
        Assert.DoesNotContain(marker, responseContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PathDiscoveryAndIniInspectionStayInsideAuthorizedRootAndRedactSecrets()
    {
        var root = Path.Combine(Path.GetTempPath(), "protheus-pulse-discovery-tests", Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "appserver");
        Directory.CreateDirectory(nested);
        var iniPath = Path.Combine(nested, "appserver.ini");
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.ini");
        const string fixtureValue = "synthetic-value-must-never-return";
        await File.WriteAllTextAsync(iniPath, $"[Network]{Environment.NewLine}Port=18080{Environment.NewLine}[Secrets]{Environment.NewLine}Password={fixtureValue}");
        await File.WriteAllTextAsync(outsidePath, "Port=1");

        try
        {
            var token = await AuthenticateDemoAdministratorAsync();
            using var discoveryRequest = AuthorizedPost("/api/v1/discovery/paths", token, new
            {
                roots = new[] { root },
                fileNames = IniFileNames,
                maxDepth = 2,
                maxResults = 10,
                timeoutSeconds = 5
            });
            var discoveryResponse = await client.SendAsync(discoveryRequest);
            discoveryResponse.EnsureSuccessStatusCode();
            var discovery = await discoveryResponse.Content.ReadAsStringAsync();
            Assert.Contains("\"dryRun\":true", discovery, StringComparison.Ordinal);
            Assert.Contains("appserver.ini", discovery, StringComparison.Ordinal);

            using var inspectRequest = AuthorizedPost("/api/v1/discovery/ini", token, new { root, path = iniPath });
            var inspectResponse = await client.SendAsync(inspectRequest);
            inspectResponse.EnsureSuccessStatusCode();
            var inspection = await inspectResponse.Content.ReadAsStringAsync();
            Assert.Contains("[REDACTED]", inspection, StringComparison.Ordinal);
            Assert.Contains("\"redactedCount\":1", inspection, StringComparison.Ordinal);
            Assert.DoesNotContain(fixtureValue, inspection, StringComparison.Ordinal);

            using var traversalRequest = AuthorizedPost("/api/v1/discovery/ini", token, new { root, path = outsidePath });
            var traversalResponse = await client.SendAsync(traversalRequest);
            Assert.Equal(HttpStatusCode.BadRequest, traversalResponse.StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            File.Delete(outsidePath);
        }
    }

    [Fact]
    public async Task DiscoveryRejectsAnonymousRequests()
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/discovery/paths", UriKind.Relative), new
        {
            roots = SampleWindowsRoots,
            fileNames = IniFileNames
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DemoModeDisablesRealCollectionAndExposesSanitizedLogEndpoint()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        using var collectRequest = AuthorizedPost("/api/v1/diagnostics/collect-now", token, new { });
        var collectResponse = await client.SendAsync(collectRequest);
        Assert.Equal(HttpStatusCode.Conflict, collectResponse.StatusCode);

        using var logRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/log-events", UriKind.Relative));
        logRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var logResponse = await client.SendAsync(logRequest);
        logResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task MonitoringCyclePersistsRealProbeAndUpdatesComponentStatus()
    {
        var root = Path.Combine(Path.GetTempPath(), "protheus-pulse-cycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var iniPath = Path.Combine(root, "appserver.ini");
        await File.WriteAllTextAsync(iniPath, "[Environment]");
        var componentId = Guid.NewGuid();
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
                dbContext.Installations.Add(new Installation
                {
                    Name = $"Ciclo real {Guid.NewGuid():N}",
                    Environment = EnvironmentKind.Development,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Components =
                    [
                        new Component
                        {
                            Id = componentId,
                            Name = "AppServer local sintético",
                            Type = ComponentType.AppServer,
                            FileTargets =
                            [
                                new FileTarget { Path = iniPath, Kind = FileTargetKind.Ini }
                            ]
                        }
                    ]
                });
                await dbContext.SaveChangesAsync();
            }

            var worker = factory.Services.GetRequiredService<MonitoringWorker>();
            var processed = await worker.RunNowAsync(CancellationToken.None);

            await using var verificationScope = factory.Services.CreateAsyncScope();
            var verificationDb = verificationScope.ServiceProvider.GetRequiredService<PulseDbContext>();
            var component = await verificationDb.Components.AsNoTracking().SingleAsync(item => item.Id == componentId);
            var probes = await verificationDb.ProbeResults.AsNoTracking()
                .Where(item => item.ComponentId == componentId)
                .ToListAsync();
            Assert.True(processed >= 1);
            Assert.Equal(HealthStatus.Healthy, component.Status);
            Assert.Contains(probes, item => item.ProbeType == ProbeType.File && item.Status == HealthStatus.Healthy);
            Assert.Contains(probes, item => item.ProbeType == ProbeType.Disk && item.Status == HealthStatus.Healthy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpRequestMessage AuthorizedPost(string path, string token, object content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(content)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
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
