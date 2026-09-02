using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.HostedServices;
using ProtheusPulse.Service.Monitoring;

namespace ProtheusPulse.IntegrationTests;

[Collection("Pulse web application")]
public sealed class PulseApiTests : IClassFixture<PulseWebApplicationFactory>
{
    private static readonly string[] IniFileNames = ["appserver.ini"];
    private static readonly string[] InitialConfigurationTags = ["piloto"];
    private static readonly string[] UpdatedConfigurationTags = ["piloto", "configurado-na-interface"];
    private static readonly string[] PilotTags = ["piloto", "servidor-a"];
    private static readonly string[] SampleWindowsRoots = ["C:\\TOTVS"];
    private static readonly string[] EmailRecipients = ["ti@exemplo.invalid", "plantao@exemplo.invalid"];
    private static readonly string[] CriticalAndUnknownStatuses = ["Critical", "Unknown"];
    private static readonly string[] WarningStatus = ["Warning"];
    private static readonly string[] HealthyStatus = ["Healthy"];
    private static string? cachedAdministratorToken;
    private readonly HttpClient client;
    private readonly PulseWebApplicationFactory factory;

    public PulseApiTests(PulseWebApplicationFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
        factory.WaitUntilReadyAsync().GetAwaiter().GetResult();
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
    public async Task AdministratorCanConfigureExistingInstallationEntirelyThroughApiAndDeleteIt()
    {
        var installationName = $"ERP configurável {Guid.NewGuid():N}";
        var token = await AuthenticateDemoAdministratorAsync();
        using var createRequest = AuthorizedPost("/api/v1/installations", token, new
        {
            name = installationName,
            environment = "Homologation",
            tags = InitialConfigurationTags,
            components = new[] { new { name = "AppServer principal", type = "AppServer", isRequired = true } }
        });
        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<InstallationResponse>();
        Assert.NotNull(created);

        using var getRequest = AuthorizedRequest(HttpMethod.Get, $"/api/v1/installations/{created.Id}/configuration", token);
        var getResponse = await client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var initialConfiguration = await getResponse.Content.ReadFromJsonAsync<InstallationConfigurationResponse>();
        Assert.NotNull(initialConfiguration);
        var existingComponent = Assert.Single(initialConfiguration.Components);
        Assert.Null(existingComponent.WindowsServiceName);

        const string executablePath = "D:\\Synthetic\\appserver.exe";
        const string iniPath = "D:\\Synthetic\\appserver.ini";
        const string logPath = "D:\\Synthetic\\logs\\console.log";
        using var updateRequest = AuthorizedRequest(HttpMethod.Put, $"/api/v1/installations/{created.Id}", token, new
        {
            name = installationName,
            environment = "Homologation",
            tags = UpdatedConfigurationTags,
            components = new[]
            {
                new
                {
                    id = existingComponent.Id,
                    name = "AppServer principal",
                    type = "AppServer",
                    isRequired = true,
                    windowsServiceName = "SyntheticAppServer",
                    executablePath,
                    iniPath,
                    logPaths = new[] { logPath },
                    tcpChecks = new[] { new { host = "127.0.0.1", port = 18080, timeoutMs = 2000, isRequired = true } },
                    httpChecks = new[]
                    {
                        new
                        {
                            url = "http://127.0.0.1:18080/health",
                            method = "GET",
                            expectedStatusMin = 200,
                            expectedStatusMax = 299,
                            timeoutMs = 3000,
                            validateTls = true,
                            certificateWarningDays = 30,
                            isRequired = false
                        }
                    }
                }
            }
        });
        var updateResponse = await client.SendAsync(updateRequest);
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<InstallationConfigurationResponse>();
        Assert.NotNull(updated);
        var configuredComponent = Assert.Single(updated.Components);
        Assert.Equal(existingComponent.Id, configuredComponent.Id);
        Assert.Equal("SyntheticAppServer", configuredComponent.WindowsServiceName);
        Assert.Equal(executablePath, configuredComponent.ExecutablePath);
        Assert.Equal(iniPath, configuredComponent.IniPath);
        Assert.Equal(logPath, Assert.Single(configuredComponent.LogPaths));
        Assert.Equal(18080, Assert.Single(configuredComponent.TcpChecks).Port);
        Assert.Equal("http://127.0.0.1:18080/health", Assert.Single(configuredComponent.HttpChecks).Url);

        await using (var verificationScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = verificationScope.ServiceProvider.GetRequiredService<PulseDbContext>();
            Assert.Equal(1, await dbContext.WindowsServiceTargets.CountAsync(item => item.ComponentId == existingComponent.Id));
            Assert.Equal(1, await dbContext.ProcessTargets.CountAsync(item => item.ComponentId == existingComponent.Id));
            Assert.Equal(2, await dbContext.FileTargets.CountAsync(item => item.ComponentId == existingComponent.Id));
            Assert.Equal(1, await dbContext.LogSources.CountAsync(item => item.ComponentId == existingComponent.Id));
            Assert.Equal(1, await dbContext.TcpChecks.CountAsync(item => item.ComponentId == existingComponent.Id));
            Assert.Equal(1, await dbContext.HttpChecks.CountAsync(item => item.ComponentId == existingComponent.Id));
            var audit = await dbContext.AuditEvents.AsNoTracking()
                .OrderByDescending(item => item.OccurredAt)
                .FirstAsync(item => item.Action == "InstallationUpdated" && item.EntityId == created.Id.ToString());
            Assert.DoesNotContain(installationName, audit.SanitizedDetailsJson, StringComparison.Ordinal);
            Assert.DoesNotContain(executablePath, audit.SanitizedDetailsJson, StringComparison.Ordinal);
        }

        using var deleteRequest = AuthorizedRequest(HttpMethod.Delete, $"/api/v1/installations/{created.Id}", token);
        var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        await using var deletionScope = factory.Services.CreateAsyncScope();
        var deletionDb = deletionScope.ServiceProvider.GetRequiredService<PulseDbContext>();
        Assert.False(await deletionDb.Installations.AnyAsync(item => item.Id == created.Id));
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
    public async Task UserManagementCreatesAccountsAndRefusesToRemoveTheLastAdministrator()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        var username = $"operador{Guid.NewGuid():N}"[..24];
        using var create = AuthorizedPost("/api/v1/users", token, new
        {
            username,
            displayName = "Operador sintético",
            password = "SenhaSintetica!2026",
            role = "Operator"
        });
        var created = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var duplicate = AuthorizedPost("/api/v1/users", token, new
        {
            username,
            password = "SenhaSintetica!2026",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(duplicate)).StatusCode);

        using var weak = AuthorizedPost("/api/v1/users", token, new
        {
            username = $"fraco{Guid.NewGuid():N}"[..20],
            password = "123",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(weak)).StatusCode);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/users", UriKind.Relative));
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var users = await listResponse.Content.ReadFromJsonAsync<UserRow[]>();
        Assert.NotNull(users);
        var administrator = Assert.Single(users, item => item.Role == "Administrator" && item.IsActive);
        Assert.DoesNotContain("PasswordHash", await listResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // O painel não pode ficar sem administrador: a tela que devolveria o perfil exige o perfil.
        using var demote = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/v1/users/{administrator.Id}", UriKind.Relative))
        {
            Content = JsonContent.Create(new { role = "Viewer" })
        };
        demote.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(demote)).StatusCode);

        using var remove = new HttpRequestMessage(HttpMethod.Delete, new Uri($"/api/v1/users/{administrator.Id}", UriKind.Relative));
        remove.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(remove)).StatusCode);
    }

    private sealed record UserRow(Guid Id, string Username, string Role, bool IsActive);

    [Fact]
    public async Task LogEventsEndpointFiltersOnTheServerBySearchLevelComponentAndPage()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        var marker = $"MARCADOR{Guid.NewGuid():N}";
        var componentId = Guid.NewGuid();
        var logSourceId = Guid.NewGuid();
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            dbContext.Installations.Add(new Installation
            {
                Name = $"Busca de logs {Guid.NewGuid():N}",
                Environment = EnvironmentKind.Development,
                CreatedAt = DateTimeOffset.UtcNow,
                Components =
                [
                    new Component
                    {
                        Id = componentId,
                        Name = "AppServer sintético de busca",
                        Type = ComponentType.AppServer,
                        LogSources = [new LogSource { Id = logSourceId, Path = "C:\\sintetico\\console.log" }]
                    }
                ]
            });
            await dbContext.SaveChangesAsync();
            dbContext.LogEvents.AddRange(
                CreateLogEvent(componentId, logSourceId, observedAt, "Error", $"{marker} falha de conexao"),
                CreateLogEvent(componentId, logSourceId, observedAt.AddMinutes(-1), "Warning", $"{marker} aviso de fila"),
                CreateLogEvent(componentId, logSourceId, observedAt.AddMinutes(-2), "Error", $"{marker} falha de indice"));
            await dbContext.SaveChangesAsync();
        }

        var all = await QueryLogEventsAsync(token, $"?search={marker}");
        Assert.Equal(3, all.Total);
        Assert.Equal(3, all.Items.Length);
        Assert.Equal(2, all.ByLevel["Error"]);
        Assert.Equal(1, all.ByLevel["Warning"]);

        var errors = await QueryLogEventsAsync(token, $"?search={marker}&level=Error");
        Assert.Equal(2, errors.Total);
        Assert.All(errors.Items, item => Assert.Equal("Error", item.Level));

        // A busca alcança o histórico inteiro, não só a página carregada.
        var term = Uri.EscapeDataString($"{marker} falha de indice");
        var single = await QueryLogEventsAsync(token, $"?search={term}");
        Assert.Equal(1, single.Total);

        var firstPage = await QueryLogEventsAsync(token, $"?search={marker}&take=2");
        Assert.Equal(3, firstPage.Total);
        Assert.Equal(2, firstPage.Items.Length);
        var secondPage = await QueryLogEventsAsync(token, $"?search={marker}&take=2&skip=2");
        Assert.Single(secondPage.Items);
        Assert.DoesNotContain(secondPage.Items[0].Id, firstPage.Items.Select(item => item.Id));

        var byComponent = await QueryLogEventsAsync(token, $"?componentId={componentId}");
        Assert.Equal(3, byComponent.Total);

        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"));
        var outOfRange = await QueryLogEventsAsync(token, $"?search={marker}&from={future}");
        Assert.Equal(0, outOfRange.Total);
        Assert.Empty(outOfRange.Items);
    }

    private static LogEvent CreateLogEvent(
        Guid componentId,
        Guid logSourceId,
        DateTimeOffset observedAt,
        string level,
        string message) => new()
        {
            ComponentId = componentId,
            LogSourceId = logSourceId,
            ObservedAt = observedAt,
            Level = level,
            Message = message,
            Fingerprint = Guid.NewGuid().ToString("N"),
            OccurrenceCount = 1
        };

    private async Task<LogEventPageResponse> QueryLogEventsAsync(string token, string queryString)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/v1/log-events{queryString}", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<LogEventPageResponse>();
        Assert.NotNull(page);
        return page;
    }

    private sealed record LogEventPageResponse(int Total, Dictionary<string, int> ByLevel, LogEventResponse[] Items);

    private sealed record LogEventResponse(Guid Id, string Level, string Message, int OccurrenceCount);

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

    [Fact]
    public async Task MaintenanceSuppressesAlertThenFailureOpensAndRecoveryResolvesIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "protheus-pulse-alert-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var missingPath = Path.Combine(root, "required.ini");
        var installationId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
                dbContext.Installations.Add(new Installation
                {
                    Id = installationId,
                    Name = $"Alertas reais {Guid.NewGuid():N}",
                    Environment = EnvironmentKind.Development,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Components =
                    [
                        new Component
                        {
                            Id = componentId,
                            Name = "Arquivo obrigatório sintético",
                            Type = ComponentType.Generic,
                            FileTargets = [new FileTarget { Path = missingPath, Kind = FileTargetKind.Ini }]
                        }
                    ]
                });
                await dbContext.SaveChangesAsync();
            }

            var token = await AuthenticateDemoAdministratorAsync();
            using var maintenanceRequest = AuthorizedPost("/api/v1/maintenance-windows", token, new
            {
                installationId,
                name = "Janela sintética",
                startsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                endsAt = DateTimeOffset.UtcNow.AddMinutes(10),
                reason = "Teste automatizado"
            });
            var maintenanceResponse = await client.SendAsync(maintenanceRequest);
            maintenanceResponse.EnsureSuccessStatusCode();
            var maintenance = await maintenanceResponse.Content.ReadFromJsonAsync<IdResponse>();
            Assert.NotNull(maintenance);

            var worker = factory.Services.GetRequiredService<MonitoringWorker>();
            await worker.RunNowAsync(CancellationToken.None);
            await using (var maintenanceScope = factory.Services.CreateAsyncScope())
            {
                var dbContext = maintenanceScope.ServiceProvider.GetRequiredService<PulseDbContext>();
                var component = await dbContext.Components.AsNoTracking().SingleAsync(item => item.Id == componentId);
                Assert.Equal(HealthStatus.Maintenance, component.Status);
                Assert.False(await dbContext.AlertOccurrences.AnyAsync(item => item.AlertRule.ComponentId == componentId));
            }

            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, new Uri($"/api/v1/maintenance-windows/{maintenance.Id}", UriKind.Relative));
            deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var deleteResponse = await client.SendAsync(deleteRequest);
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            await worker.RunNowAsync(CancellationToken.None);
            await worker.RunNowAsync(CancellationToken.None);
            Guid occurrenceId;
            await using (var alertScope = factory.Services.CreateAsyncScope())
            {
                var dbContext = alertScope.ServiceProvider.GetRequiredService<PulseDbContext>();
                var occurrence = await dbContext.AlertOccurrences.AsNoTracking()
                    .SingleAsync(item => item.AlertRule.ComponentId == componentId
                        && item.AlertRule.ProbeType == ProbeType.File
                        && item.State == AlertState.Active);
                occurrenceId = occurrence.Id;
            }

            using var acknowledgeRequest = AuthorizedPost($"/api/v1/alerts/{occurrenceId}/acknowledge", token, new { });
            var acknowledgeResponse = await client.SendAsync(acknowledgeRequest);
            Assert.Equal(HttpStatusCode.NoContent, acknowledgeResponse.StatusCode);

            await File.WriteAllTextAsync(missingPath, "[Environment]");
            await worker.RunNowAsync(CancellationToken.None);
            await using var recoveryScope = factory.Services.CreateAsyncScope();
            var recoveryDb = recoveryScope.ServiceProvider.GetRequiredService<PulseDbContext>();
            var resolved = await recoveryDb.AlertOccurrences.AsNoTracking().SingleAsync(item => item.Id == occurrenceId);
            Assert.Equal(AlertState.Resolved, resolved.State);
            Assert.NotNull(resolved.ResolvedAt);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AdministratorCreatesEditsAndRemovesAlertRuleFromTheDashboard()
    {
        var installationId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            dbContext.Installations.Add(new Installation
            {
                Id = installationId,
                Name = $"Regras na interface {Guid.NewGuid():N}",
                Environment = EnvironmentKind.Development,
                CreatedAt = DateTimeOffset.UtcNow,
                Components = [new Component { Id = componentId, Name = "Broker sintético", Type = ComponentType.Broker }]
            });
            await dbContext.SaveChangesAsync();
        }

        var token = await AuthenticateDemoAdministratorAsync();
        using var createRequest = AuthorizedPost("/api/v1/alert-rules", token, new
        {
            componentId,
            name = "Broker fora do ar",
            probeType = "Tcp",
            severity = "Critical",
            minimumConsecutiveFailures = 3,
            cooldownSeconds = 900,
            triggerStatuses = CriticalAndUnknownStatuses
        });
        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<IdResponse>();
        Assert.NotNull(created);

        // O editor reabre a regra a partir da listagem, então ela precisa devolver os estados de disparo.
        var listed = await ReadAlertRuleAsync(token, created.Id);
        Assert.Equal("Tcp", listed.ProbeType);
        Assert.Equal("Critical", listed.Severity);
        Assert.Equal(3, listed.MinimumConsecutiveFailures);
        Assert.Equal(900, listed.CooldownSeconds);
        Assert.Equal(CriticalAndUnknownStatuses, listed.TriggerStatuses);
        Assert.Equal(installationId, listed.InstallationId);
        Assert.False(listed.IsAutomatic);

        using var updateRequest = AuthorizedRequest(HttpMethod.Put, $"/api/v1/alert-rules/{created.Id}", token, new
        {
            name = "Broker instável",
            severity = "Warning",
            enabled = false,
            minimumConsecutiveFailures = 2,
            cooldownSeconds = 300,
            triggerStatuses = WarningStatus
        });
        var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var updated = await ReadAlertRuleAsync(token, created.Id);
        Assert.Equal("Broker instável", updated.Name);
        Assert.Equal("Warning", updated.Severity);
        Assert.False(updated.Enabled);
        Assert.Equal(2, updated.MinimumConsecutiveFailures);
        Assert.Equal(WarningStatus, updated.TriggerStatuses);
        Assert.Equal("Tcp", updated.ProbeType);

        using var invalidRequest = AuthorizedRequest(HttpMethod.Put, $"/api/v1/alert-rules/{created.Id}", token, new
        {
            name = "Broker instável",
            severity = "Warning",
            enabled = true,
            minimumConsecutiveFailures = 2,
            cooldownSeconds = 300,
            triggerStatuses = HealthyStatus
        });
        var invalidResponse = await client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        using var deleteRequest = AuthorizedRequest(HttpMethod.Delete, $"/api/v1/alert-rules/{created.Id}", token);
        var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var remaining = await ReadAlertRulesAsync(token);
        Assert.DoesNotContain(remaining, item => item.Id == created.Id);
    }

    [Fact]
    public async Task ServerTargetIsCollectedForAlertingAndKeptOutOfTheInstallationList()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        Guid installationId;
        Guid componentId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            var installation = await dbContext.Installations.AsNoTracking()
                .Include(item => item.Components)
                .SingleAsync(item => item.IsSystem);
            installationId = installation.Id;
            componentId = Assert.Single(installation.Components).Id;
        }

        // Processador, memória e disco precisam virar probe: é o que dá regra e ocorrência.
        var worker = factory.Services.GetRequiredService<MonitoringWorker>();
        await worker.RunNowAsync(CancellationToken.None);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            var probeTypes = await dbContext.ProbeResults.AsNoTracking()
                .Where(item => item.ComponentId == componentId)
                .Select(item => item.ProbeType)
                .Distinct()
                .ToListAsync();
            Assert.Contains(ProbeType.ServerCpu, probeTypes);
            Assert.Contains(ProbeType.ServerMemory, probeTypes);
            Assert.Contains(ProbeType.ServerDisk, probeTypes);

            // O primeiro ciclo já deixa uma regra padrão por verificação, pronta para editar na tela.
            var ruleNames = await dbContext.AlertRules.AsNoTracking()
                .Where(item => item.ComponentId == componentId)
                .Select(item => item.Name)
                .ToListAsync();
            Assert.Contains("Memória do servidor", ruleNames);
        }

        using var installationsRequest = AuthorizedRequest(HttpMethod.Get, "/api/v1/installations", token);
        var installationsResponse = await client.SendAsync(installationsRequest);
        installationsResponse.EnsureSuccessStatusCode();
        Assert.DoesNotContain(
            installationId.ToString(),
            await installationsResponse.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        using var deleteRequest = AuthorizedRequest(HttpMethod.Delete, $"/api/v1/installations/{installationId}", token);
        var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task ServerRuleAcceptsAPercentThresholdAndRejectsItOnOtherProbes()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        Guid serverComponentId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            serverComponentId = await dbContext.Components.AsNoTracking()
                .Where(item => item.Installation.IsSystem)
                .Select(item => item.Id)
                .SingleAsync();
        }

        using var createRequest = AuthorizedPost("/api/v1/alert-rules", token, new
        {
            componentId = serverComponentId,
            name = $"Memória acima de 90% {Guid.NewGuid():N}",
            probeType = "ServerMemory",
            severity = "Warning",
            minimumConsecutiveFailures = 3,
            cooldownSeconds = 1_800,
            triggerStatuses = WarningStatus,
            thresholdPercent = 90
        });
        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<IdResponse>();
        Assert.NotNull(created);

        var listed = await ReadAlertRuleAsync(token, created.Id);
        Assert.Equal(90, listed.ThresholdPercent);

        // Verificação de componente não tem uso em percentual para comparar.
        using var invalidRequest = AuthorizedPost("/api/v1/alert-rules", token, new
        {
            componentId = serverComponentId,
            name = "Limite onde não cabe",
            probeType = "Tcp",
            severity = "Warning",
            minimumConsecutiveFailures = 2,
            cooldownSeconds = 300,
            triggerStatuses = WarningStatus,
            thresholdPercent = 90
        });
        var invalidResponse = await client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        using var outOfRangeRequest = AuthorizedPost("/api/v1/alert-rules", token, new
        {
            componentId = serverComponentId,
            name = "Limite fora da faixa",
            probeType = "ServerDisk",
            severity = "Warning",
            minimumConsecutiveFailures = 2,
            cooldownSeconds = 300,
            triggerStatuses = WarningStatus,
            thresholdPercent = 140
        });
        var outOfRangeResponse = await client.SendAsync(outOfRangeRequest);
        Assert.Equal(HttpStatusCode.BadRequest, outOfRangeResponse.StatusCode);

        using var deleteRequest = AuthorizedRequest(HttpMethod.Delete, $"/api/v1/alert-rules/{created.Id}", token);
        var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task AlertHistoryIsPagedInsteadOfCutAtTheDashboardSummary()
    {
        var componentId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            dbContext.Installations.Add(new Installation
            {
                Name = $"Histórico {Guid.NewGuid():N}",
                Environment = EnvironmentKind.Development,
                CreatedAt = now,
                Components = [new Component { Id = componentId, Name = "Componente com histórico", Type = ComponentType.Generic }]
            });
            dbContext.AlertRules.Add(new AlertRule
            {
                Id = ruleId,
                ComponentId = componentId,
                Name = "Regra com histórico",
                RuleKey = $"CUSTOM-{Guid.NewGuid():N}",
                ProbeType = ProbeType.Tcp,
                Severity = AlertSeverity.Warning
            });
            // Doze ocorrências: mais que as oito que o resumo do painel devolve.
            for (var index = 0; index < 12; index++)
            {
                dbContext.AlertOccurrences.Add(new AlertOccurrence
                {
                    AlertRuleId = ruleId,
                    State = index == 0 ? AlertState.Active : AlertState.Resolved,
                    StartedAt = now.AddMinutes(-index),
                    ResolvedAt = index == 0 ? null : now.AddMinutes(-index).AddSeconds(30),
                    Evidence = $"Ocorrência sintética {index}"
                });
            }

            await dbContext.SaveChangesAsync();
        }

        var token = await AuthenticateDemoAdministratorAsync();
        var firstPage = await ReadAlertPageAsync(token, $"componentId={componentId}&take=5");
        Assert.Equal(12, firstPage.Total);
        Assert.Equal(5, firstPage.Items.Count);
        Assert.Equal(1, firstPage.ByState["Active"]);
        Assert.Equal(11, firstPage.ByState["Resolved"]);

        var secondPage = await ReadAlertPageAsync(token, $"componentId={componentId}&take=5&skip=5");
        Assert.Equal(5, secondPage.Items.Count);
        Assert.Empty(secondPage.Items.Select(item => item.Id).Intersect(firstPage.Items.Select(item => item.Id)));

        var resolved = await ReadAlertPageAsync(token, $"componentId={componentId}&state=Resolved&take=200");
        Assert.Equal(11, resolved.Total);
        Assert.All(resolved.Items, item => Assert.Equal("Resolved", item.State));
        // A contagem por estado ignora o filtro, porque alimenta os próprios botões.
        Assert.Equal(1, resolved.ByState["Active"]);

        using var invalid = AuthorizedRequest(HttpMethod.Get, "/api/v1/alerts?state=NaoExiste", token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalid)).StatusCode);
    }

    [Fact]
    public async Task HttpsRefusesToSaveACertificateThatWouldNotServe()
    {
        var token = await AuthenticateDemoAdministratorAsync();

        // Ligar HTTPS sem certificado deixaria o serviço sem endpoint utilizável.
        using var withoutCertificate = AuthorizedRequest(HttpMethod.Put, "/api/v1/settings/network", token, new
        {
            allowRemoteAccess = false,
            port = 5058,
            useHttps = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(withoutCertificate)).StatusCode);

        using var missingFile = AuthorizedRequest(HttpMethod.Put, "/api/v1/settings/network", token, new
        {
            allowRemoteAccess = false,
            port = 5058,
            useHttps = true,
            certificatePath = Path.Combine(Path.GetTempPath(), $"nao-existe-{Guid.NewGuid():N}.pfx")
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(missingFile)).StatusCode);
    }

    [Fact]
    public async Task SelfSignedCertificateIsUsableForHttps()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        using var generateRequest = AuthorizedPost("/api/v1/settings/network/self-signed", token, new { });
        var generateResponse = await client.SendAsync(generateRequest);
        generateResponse.EnsureSuccessStatusCode();
        var created = await generateResponse.Content.ReadFromJsonAsync<SelfSignedResponse>();
        Assert.NotNull(created);
        Assert.True(File.Exists(created.Path));
        Assert.True(created.NotAfter > DateTime.UtcNow.AddYears(1));

        // O que é gerado tem de passar na mesma conferência que a gravação faz.
        using var saveRequest = AuthorizedRequest(HttpMethod.Put, "/api/v1/settings/network", token, new
        {
            allowRemoteAccess = false,
            port = 5058,
            useHttps = true,
            certificatePath = created.Path
        });
        var saveResponse = await client.SendAsync(saveRequest);
        saveResponse.EnsureSuccessStatusCode();
        var settings = await saveResponse.Content.ReadFromJsonAsync<NetworkResponse>();
        Assert.NotNull(settings);
        Assert.True(settings.UseHttps);
        Assert.True(settings.CertificateValid);
        Assert.StartsWith("https://", settings.BoundUrl, StringComparison.Ordinal);

        // Volta para HTTP para não afetar os demais testes desta coleção.
        using var restore = AuthorizedRequest(HttpMethod.Put, "/api/v1/settings/network", token, new
        {
            allowRemoteAccess = false,
            port = 5058,
            useHttps = false
        });
        (await client.SendAsync(restore)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ServerThresholdsAreEditableAndTakeEffectWithoutRestart()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        using var saveRequest = AuthorizedRequest(HttpMethod.Put, "/api/v1/settings/server-thresholds", token, new
        {
            cpuWarningPercent = 70,
            cpuCriticalPercent = 88,
            memoryWarningPercent = 75,
            memoryCriticalPercent = 90,
            diskFreeWarningPercent = 20,
            diskFreeCriticalPercent = 8
        });
        var saveResponse = await client.SendAsync(saveRequest);
        saveResponse.EnsureSuccessStatusCode();

        // O ajuste vale no ciclo seguinte: as opções que os coletores já têm são atualizadas.
        var serverOptions = factory.Services.GetRequiredService<ServerResourceOptions>();
        var probeOptions = factory.Services.GetRequiredService<ProbeCollectorOptions>();
        Assert.Equal(70, serverOptions.CpuWarningPercent);
        Assert.Equal(90, serverOptions.MemoryCriticalPercent);
        Assert.Equal(20, serverOptions.DiskWarningPercent);
        Assert.Equal(8, probeOptions.DiskCriticalPercent);

        using var readRequest = AuthorizedRequest(HttpMethod.Get, "/api/v1/settings/server-thresholds", token);
        var read = await client.SendAsync(readRequest);
        read.EnsureSuccessStatusCode();
        var stored = await read.Content.ReadFromJsonAsync<ServerThresholdResponse>();
        Assert.NotNull(stored);
        Assert.Equal(70, stored.CpuWarningPercent);
        Assert.NotNull(stored.UpdatedAt);

        // Disco é medido pelo espaço livre: o crítico não pode ficar acima da atenção.
        using var invalid = AuthorizedRequest(HttpMethod.Put, "/api/v1/settings/server-thresholds", token, new
        {
            cpuWarningPercent = 70,
            cpuCriticalPercent = 88,
            memoryWarningPercent = 75,
            memoryCriticalPercent = 90,
            diskFreeWarningPercent = 5,
            diskFreeCriticalPercent = 20
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalid)).StatusCode);
    }

    [Fact]
    public async Task AuditTrailIsReadableAndCarriesWhoDidWhat()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        var channelName = $"Auditoria {Guid.NewGuid():N}";
        using var createRequest = AuthorizedPost("/api/v1/notification-channels", token, new
        {
            name = channelName,
            type = "Webhook",
            url = "https://notify.example.invalid/hooks/auditoria",
            enabled = false
        });
        (await client.SendAsync(createRequest)).EnsureSuccessStatusCode();

        using var request = AuthorizedRequest(HttpMethod.Get, "/api/v1/audit?action=NotificationChannelCreated&take=200", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<AuditPageResponse>();
        Assert.NotNull(page);
        Assert.True(page.Total > 0);
        Assert.All(page.Items, item => Assert.Equal("NotificationChannelCreated", item.Action));

        var entry = page.Items[0];
        Assert.Equal("NotificationChannel", entry.EntityType);
        Assert.Equal("demo.admin", entry.Username);
        Assert.NotNull(entry.UserDisplayName);
        // O nome do canal não entra na auditoria; só o tipo e o estado, já sanitizados.
        Assert.DoesNotContain(channelName, entry.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("Webhook", entry.Details ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditTrailIsClosedToNonAdministrators()
    {
        var administratorToken = await AuthenticateDemoAdministratorAsync();
        var username = $"leitor{Guid.NewGuid():N}"[..18];
        const string password = "SenhaDeTeste!2026";
        using var createUser = AuthorizedPost("/api/v1/users", administratorToken, new
        {
            username,
            displayName = "Leitor de plantão",
            password,
            role = "Viewer"
        });
        (await client.SendAsync(createUser)).EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new { username, password });
        login.EnsureSuccessStatusCode();
        var viewer = await login.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(viewer);

        using var request = AuthorizedRequest(HttpMethod.Get, "/api/v1/audit", viewer.AccessToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NotificationChannelConfigurationIsProtectedAndNeverReturned()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        const string endpoint = "https://notify.example.invalid/hooks/synthetic";
        using var createRequest = AuthorizedPost("/api/v1/notification-channels", token, new
        {
            name = "Webhook sintético",
            type = "Webhook",
            url = endpoint,
            enabled = false
        });
        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<IdResponse>();
        Assert.NotNull(created);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            var channel = await dbContext.NotificationChannels.AsNoTracking().SingleAsync(item => item.Id == created.Id);
            Assert.DoesNotContain(endpoint, channel.ProtectedConfiguration, StringComparison.Ordinal);
        }

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/notification-channels", UriKind.Relative));
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResponse = await client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        Assert.DoesNotContain(endpoint, await getResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetentionAggregatesMetricsAndDeletesExpiredDetailedHistory()
    {
        var componentId = Guid.NewGuid();
        var logSourceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var oldHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddDays(-8);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            var component = new Component
            {
                Id = componentId,
                Name = "Retenção sintética",
                Type = ComponentType.Generic,
                LogSources = [new LogSource { Id = logSourceId, Path = "D:\\Synthetic\\console.log" }]
            };
            dbContext.Installations.Add(new Installation
            {
                Name = $"Retenção {Guid.NewGuid():N}",
                Environment = EnvironmentKind.Development,
                CreatedAt = now,
                Components = [component]
            });
            dbContext.MetricSamples.AddRange(
                new MetricSample { ComponentId = componentId, Name = "latency", Unit = "ms", Value = 10, ObservedAt = oldHour.AddMinutes(5) },
                new MetricSample { ComponentId = componentId, Name = "latency", Unit = "ms", Value = 30, ObservedAt = oldHour.AddMinutes(10) });
            dbContext.ProbeResults.Add(new ProbeResult
            {
                ComponentId = componentId,
                ProbeType = ProbeType.File,
                Status = HealthStatus.Healthy,
                ObservedAt = now.AddDays(-31),
                DurationMs = 1,
                Message = "Histórico expirado"
            });
            dbContext.LogEvents.Add(new LogEvent
            {
                ComponentId = componentId,
                LogSourceId = logSourceId,
                ObservedAt = now.AddDays(-31),
                Level = "Information",
                Message = "Evento expirado",
                Fingerprint = new string('A', 64)
            });
            await dbContext.SaveChangesAsync();
        }

        var retention = factory.Services.GetRequiredService<RetentionService>();
        var result = await retention.RunAsync(CancellationToken.None);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<PulseDbContext>();
        Assert.True(result.DetailedMetricsAggregated >= 2);
        Assert.False(await verificationDb.ProbeResults.AnyAsync(item => item.ComponentId == componentId));
        Assert.False(await verificationDb.LogEvents.AnyAsync(item => item.ComponentId == componentId));
        var aggregate = await verificationDb.MetricSamples.AsNoTracking()
            .SingleAsync(item => item.ComponentId == componentId && item.AggregationWindow != null);
        Assert.Equal(20, aggregate.Value);
        Assert.Equal(TimeSpan.FromHours(1), aggregate.AggregationWindow);
    }

    [Fact]
    public async Task HeartbeatTokenIsShownOnceHashedRotatedAndRequiredForIngestion()
    {
        var componentId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            dbContext.Installations.Add(new Installation
            {
                Name = $"Heartbeat {Guid.NewGuid():N}",
                Environment = EnvironmentKind.Development,
                CreatedAt = DateTimeOffset.UtcNow,
                Components =
                [
                    new Component
                    {
                        Id = componentId,
                        Name = "Job sintético",
                        Type = ComponentType.Job
                    }
                ]
            });
            await dbContext.SaveChangesAsync();
        }

        var administratorToken = await AuthenticateDemoAdministratorAsync();
        using var createRequest = AuthorizedPost("/api/v1/heartbeat-definitions", administratorToken, new
        {
            componentId,
            name = "Rotina agendada sintética",
            expectedIntervalSeconds = 300,
            toleranceSeconds = 60
        });
        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<HeartbeatTokenResponse>();
        Assert.NotNull(created);
        Assert.True(created.TokenShownOnce);

        await using (var verificationScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = verificationScope.ServiceProvider.GetRequiredService<PulseDbContext>();
            var stored = await dbContext.HeartbeatDefinitions.AsNoTracking().SingleAsync(item => item.Id == created.Id);
            Assert.Equal(64, stored.TokenHash?.Length);
            Assert.DoesNotContain(created.Token, stored.TokenHash, StringComparison.Ordinal);
        }

        var invalid = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/heartbeats/{created.JobKey}", UriKind.Relative));
        invalid.Headers.Add("X-Pulse-Heartbeat-Token", "invalid-synthetic-token-value");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(invalid)).StatusCode);

        using var accepted = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/heartbeats/{created.JobKey}", UriKind.Relative));
        accepted.Headers.Add("X-Pulse-Heartbeat-Token", created.Token);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(accepted)).StatusCode);

        using var rotateRequest = AuthorizedPost($"/api/v1/heartbeat-definitions/{created.Id}/rotate", administratorToken, new { });
        var rotateResponse = await client.SendAsync(rotateRequest);
        rotateResponse.EnsureSuccessStatusCode();
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<HeartbeatTokenResponse>();
        Assert.NotNull(rotated);
        Assert.NotEqual(created.Token, rotated.Token);

        using var rejectedOldToken = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/heartbeats/{created.JobKey}", UriKind.Relative));
        rejectedOldToken.Headers.Add("X-Pulse-Heartbeat-Token", created.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(rejectedOldToken)).StatusCode);

        using var acceptedNewToken = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/heartbeats/{created.JobKey}", UriKind.Relative));
        acceptedNewToken.Headers.Add("X-Pulse-Heartbeat-Token", rotated.Token);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(acceptedNewToken)).StatusCode);

        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var definition = await finalDb.HeartbeatDefinitions.AsNoTracking().SingleAsync(item => item.Id == created.Id);
        Assert.NotNull(definition.LastHeartbeatAt);
        Assert.Equal(2, await finalDb.ProbeResults.CountAsync(item => item.ComponentId == componentId && item.ProbeType == ProbeType.Heartbeat));
    }

    [Fact]
    public async Task ServerResourcesRequireAuthenticationAndDescribeTheLocalMachine()
    {
        var anonymous = await client.GetAsync(new Uri("/api/v1/server/resources", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var token = await AuthenticateDemoAdministratorAsync();
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/v1/server/resources", token);
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ServerResourcesResponse>();
        Assert.NotNull(payload);
        Assert.Equal(Environment.MachineName, payload.Server.HostName);
        Assert.Equal(Environment.ProcessorCount, payload.Server.ProcessorCount);
        Assert.True(payload.Thresholds.CpuWarningPercent < payload.Thresholds.CpuCriticalPercent);
        Assert.All(payload.Server.Disks, disk => Assert.True(disk.TotalBytes > 0));
    }

    [Fact]
    public async Task EmailSettingsRoundTripWithoutEverReturningThePassword()
    {
        var token = await AuthenticateDemoAdministratorAsync();
        const string smtpPassword = "synthetic-smtp-password-value";
        using var saveRequest = AuthorizedRequest(HttpMethod.Put, "/api/v1/settings/email", token, new
        {
            enabled = false,
            host = "smtp.exemplo.invalid",
            port = 465,
            security = "SslOnConnect",
            username = "pulse@exemplo.invalid",
            password = smtpPassword,
            fromAddress = "pulse@exemplo.invalid",
            fromName = "Protheus Pulse",
            recipients = EmailRecipients,
            timeoutSeconds = 20,
            allowInvalidCertificate = false,
            notifyAlerts = true,
            notifyLogErrors = true
        });
        var saveResponse = await client.SendAsync(saveRequest);
        Assert.Equal(HttpStatusCode.NoContent, saveResponse.StatusCode);

        using var getRequest = AuthorizedRequest(HttpMethod.Get, "/api/v1/settings/email", token);
        var getResponse = await client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var body = await getResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(smtpPassword, body, StringComparison.Ordinal);
        Assert.Contains("\"hasPassword\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"port\":465", body, StringComparison.Ordinal);
        Assert.Contains("\"security\":\"SslOnConnect\"", body, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var channel = await dbContext.NotificationChannels.AsNoTracking()
            .SingleAsync(item => item.Type == NotificationChannelType.Smtp);
        Assert.DoesNotContain(smtpPassword, channel.ProtectedConfiguration, StringComparison.Ordinal);

        using var invalidRequest = AuthorizedRequest(HttpMethod.Put, "/api/v1/settings/email", token, new
        {
            enabled = true,
            host = "smtp.exemplo.invalid",
            port = 70_000,
            security = "StartTls",
            fromAddress = "sem-arroba",
            recipients = Array.Empty<string>(),
            timeoutSeconds = 20
        });
        var invalidResponse = await client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task LogErrorsCollectedInternallyAreSanitizedAndQueuedForEmail()
    {
        var root = Path.Combine(Path.GetTempPath(), "protheus-pulse-log-mail-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var logPath = Path.Combine(root, "console.log");
        const string secret = "synthetic-secret-must-not-persist";
        var componentId = Guid.NewGuid();
        try
        {
            await File.WriteAllTextAsync(logPath, $"[TOTVS][INFO ] AppServer iniciado{Environment.NewLine}");
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
                dbContext.Installations.Add(new Installation
                {
                    Name = $"Erros de log {Guid.NewGuid():N}",
                    Environment = EnvironmentKind.Development,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Components =
                    [
                        new Component
                        {
                            Id = componentId,
                            Name = "AppServer sintético",
                            Type = ComponentType.AppServer,
                            LogSources = [new LogSource { Path = logPath }]
                        }
                    ]
                });
                await dbContext.SaveChangesAsync();
            }

            // Primeiro ciclo posiciona o cursor no fim do arquivo.
            var worker = factory.Services.GetRequiredService<MonitoringWorker>();
            await worker.RunNowAsync(CancellationToken.None);
            var buffer = factory.Services.GetRequiredService<LogAlertMailBuffer>();
            while (buffer.TryRead(out _))
            {
                // Descarta o que outros testes possam ter deixado na fila.
            }

            await File.AppendAllTextAsync(
                logPath,
                $"[TOTVS][ERROR] Thread Error ao conectar; password={secret}{Environment.NewLine}");
            await worker.RunNowAsync(CancellationToken.None);

            Assert.True(buffer.TryRead(out var notice));
            Assert.NotNull(notice);
            Assert.Equal("Error", notice.Level);
            Assert.Equal("AppServer sintético", notice.ComponentName);
            Assert.DoesNotContain(secret, notice.Message, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", notice.Message, StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(notice.Fingerprint));

            await using var verificationScope = factory.Services.CreateAsyncScope();
            var verificationDb = verificationScope.ServiceProvider.GetRequiredService<PulseDbContext>();
            var stored = await verificationDb.LogEvents.AsNoTracking()
                .SingleAsync(item => item.ComponentId == componentId && item.Level == "Error");
            Assert.DoesNotContain(secret, stored.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpRequestMessage AuthorizedPost(string path, string token, object content)
    {
        return AuthorizedRequest(HttpMethod.Post, path, token, content);
    }

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string path, string token, object? content = null)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = content is null ? null : JsonContent.Create(content)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<string> AuthenticateDemoAdministratorAsync()
    {
        if (cachedAdministratorToken is not null)
        {
            return cachedAdministratorToken;
        }

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
        cachedAdministratorToken = token.AccessToken;
        return cachedAdministratorToken;
    }

    private async Task<AlertPageResponse> ReadAlertPageAsync(string token, string query)
    {
        using var request = AuthorizedRequest(HttpMethod.Get, $"/api/v1/alerts?{query}", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<AlertPageResponse>();
        Assert.NotNull(page);
        return page;
    }

    private async Task<AlertRuleResponse> ReadAlertRuleAsync(string token, Guid id)
    {
        var rules = await ReadAlertRulesAsync(token);
        return Assert.Single(rules, item => item.Id == id);
    }

    private async Task<IReadOnlyList<AlertRuleResponse>> ReadAlertRulesAsync(string token)
    {
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/v1/alert-rules", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var rules = await response.Content.ReadFromJsonAsync<List<AlertRuleResponse>>();
        Assert.NotNull(rules);
        return rules;
    }

    private sealed record TokenResponse(string AccessToken);
    private sealed record AuthStatusResponse(string DemoUsername, string DemoPassword);
    private sealed record ComponentPayload(string Name, string Type, bool IsRequired = true);
    private sealed record InstallationResponse(Guid Id, string Name, string Environment, int ComponentCount, string Status);
    private sealed record InstallationConfigurationResponse(Guid Id, string Name, List<ComponentConfigurationResponse> Components);
    private sealed record ComponentConfigurationResponse(
        Guid Id,
        string? WindowsServiceName,
        string? ExecutablePath,
        string? IniPath,
        List<string> LogPaths,
        List<TcpCheckConfigurationResponse> TcpChecks,
        List<HttpCheckConfigurationResponse> HttpChecks);
    private sealed record TcpCheckConfigurationResponse(string Host, int Port);
    private sealed record HttpCheckConfigurationResponse(string Url);
    private sealed record IdResponse(Guid Id);
    private sealed record SelfSignedResponse(string Path, string Subject, DateTime NotAfter);
    private sealed record NetworkResponse(
        bool AllowRemoteAccess,
        int Port,
        string BoundUrl,
        List<string> LocalAddresses,
        bool UseHttps,
        string? CertificatePath,
        bool HasCertificatePassword,
        bool? CertificateValid);
    private sealed record ServerThresholdResponse(
        double CpuWarningPercent,
        double CpuCriticalPercent,
        double MemoryWarningPercent,
        double MemoryCriticalPercent,
        double DiskFreeWarningPercent,
        double DiskFreeCriticalPercent,
        DateTimeOffset? UpdatedAt);
    private sealed record AlertPageResponse(int Total, Dictionary<string, int> ByState, List<AlertEntryResponse> Items);
    private sealed record AlertEntryResponse(Guid Id, string RuleName, string Severity, string State, DateTimeOffset StartedAt);
    private sealed record AuditPageResponse(int Total, Dictionary<string, int> ByAction, List<AuditEntryResponse> Items);
    private sealed record AuditEntryResponse(
        Guid Id,
        string Action,
        string EntityType,
        string? EntityId,
        DateTimeOffset OccurredAt,
        string? RemoteAddress,
        string? Details,
        string? UserDisplayName,
        string? Username);
    private sealed record AlertRuleResponse(
        Guid Id,
        Guid ComponentId,
        Guid InstallationId,
        string InstallationName,
        string ComponentName,
        string Name,
        string ProbeType,
        string Severity,
        bool Enabled,
        int MinimumConsecutiveFailures,
        int CooldownSeconds,
        List<string> TriggerStatuses,
        double? ThresholdPercent,
        bool IsAutomatic);
    private sealed record HeartbeatTokenResponse(Guid Id, string JobKey, string Token, bool TokenShownOnce);
    private sealed record ServerResourcesResponse(ServerSnapshotResponse Server, ServerThresholdsResponse Thresholds);
    private sealed record ServerSnapshotResponse(
        string HostName,
        int ProcessorCount,
        long UptimeSeconds,
        List<ServerDiskResponse> Disks);
    private sealed record ServerDiskResponse(string Name, long TotalBytes, double FreePercent);
    private sealed record ServerThresholdsResponse(double CpuWarningPercent, double CpuCriticalPercent);
}

public sealed class PulseWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "protheus-pulse-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Pulse:DemoMode", "true");
        builder.UseSetting("Pulse:DataDirectory", dataDirectory);
        builder.UseSetting("Pulse:DiskWarningPercent", "1");
        builder.UseSetting("Pulse:DiskCriticalPercent", "0");
    }

    /// <summary>
    /// A migração roda em segundo plano para não estourar a janela do SCM, então o host
    /// sobe antes do esquema existir. Os testes esperam a mesma prontidão que o instalador
    /// espera pelo /health/ready.
    /// </summary>
    public async Task WaitUntilReadyAsync()
    {
        var state = Services.GetRequiredService<DatabaseReadyState>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await state.WaitAsync(timeout.Token);
    }
}
