using ProtheusPulse.Service.Configuration;

namespace ProtheusPulse.UnitTests;

public sealed class NetworkOptionsTests
{
    [Fact]
    public void LoopbackIsTheDefaultAndTheHostFilterStaysStrict()
    {
        var overrides = new NetworkOptions().BuildOverrides();

        Assert.Equal("http://127.0.0.1:5058", overrides["Kestrel:Endpoints:Http:Url"]);
        Assert.False(overrides.ContainsKey("AllowedHosts"));
    }

    [Fact]
    public void RemoteAccessOverridesTheKestrelEndpointAndTheHostFilter()
    {
        var overrides = new NetworkOptions { AllowRemoteAccess = true, Port = 8080 }.BuildOverrides();

        // Sem a chave do Kestrel o UseUrls seria ignorado e o serviço ficaria em loopback;
        // sem AllowedHosts a requisição pelo IP do servidor voltaria 400.
        Assert.Equal("http://0.0.0.0:8080", overrides["Kestrel:Endpoints:Http:Url"]);
        Assert.Equal("*", overrides["AllowedHosts"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(1_023)]
    [InlineData(65_536)]
    public void PortsOutsideTheAllowedRangeAreRejected(int port)
    {
        Assert.NotEmpty(new NetworkOptions { Port = port }.Validate());
    }

    [Theory]
    [InlineData(1_024)]
    [InlineData(5_058)]
    [InlineData(65_535)]
    public void PortsInsideTheAllowedRangeAreAccepted(int port)
    {
        Assert.Empty(new NetworkOptions { Port = port }.Validate());
    }
}
