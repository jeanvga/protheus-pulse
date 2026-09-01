using ProtheusPulse.Service.Configuration;

namespace ProtheusPulse.UnitTests;

public sealed class ObservabilityOptionsTests
{
    [Fact]
    public void DefaultsAreDisabledAndSafeForLocalAlloy()
    {
        var options = new ObservabilityOptions();

        Assert.False(options.Enabled);
        Assert.Equal("http://127.0.0.1:4318", options.OtlpEndpoint);
        Assert.Equal("protheus", options.ServiceNamespace);
        Assert.Equal(10, options.ExportIntervalSeconds);
        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData("http://127.0.0.1:4318")]
    [InlineData("http://localhost:4318")]
    [InlineData("http://[::1]:4318")]
    [InlineData("https://alloy.observability.intra:4318")]
    public void ValidateAcceptsSecureEndpoints(string endpoint)
    {
        var options = Enabled(endpoint);

        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData("http://10.0.0.20:4318")]
    [InlineData("http://alloy.observability.intra:4318")]
    public void ValidateRejectsPlainHttpOutsideLoopback(string endpoint)
    {
        var errors = Enabled(endpoint).Validate();

        Assert.Contains(errors, error => error.Contains("loopback", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("alloy:4318")]
    [InlineData("ftp://localhost:4318")]
    [InlineData("https://user:password@alloy.intra:4318")]
    [InlineData("https://alloy.intra:4318?token=secret")]
    [InlineData("https://alloy.intra:4318/#fragment")]
    public void ValidateRejectsInvalidOrSensitiveEndpointForms(string endpoint)
    {
        Assert.NotEmpty(Enabled(endpoint).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void ValidateRejectsExportIntervalOutsideLimits(int interval)
    {
        var options = Enabled("http://127.0.0.1:4318");
        options.ExportIntervalSeconds = interval;

        Assert.Contains(options.Validate(), error => error.Contains("ExportIntervalSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains spaces")]
    [InlineData("contains/slash")]
    public void ValidateRejectsInvalidServiceNamespace(string serviceNamespace)
    {
        var options = Enabled("http://127.0.0.1:4318");
        options.ServiceNamespace = serviceNamespace;

        Assert.Contains(options.Validate(), error => error.Contains("ServiceNamespace", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateIgnoresExporterFieldsWhenDisabled()
    {
        var options = new ObservabilityOptions
        {
            Enabled = false,
            OtlpEndpoint = "not-an-endpoint",
            ServiceNamespace = string.Empty,
            ExportIntervalSeconds = 0
        };

        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData("http://127.0.0.1:4318", "http://127.0.0.1:4318/v1/metrics")]
    [InlineData("https://alloy.intra/otlp/", "https://alloy.intra/otlp/v1/metrics")]
    [InlineData("https://alloy.intra/v1/metrics", "https://alloy.intra/v1/metrics")]
    public void GetMetricsEndpointAppendsTheOtlpSignalPathOnce(string configured, string expected)
    {
        var options = Enabled(configured);

        Assert.Equal(new Uri(expected), options.GetMetricsEndpoint());
    }

    private static ObservabilityOptions Enabled(string endpoint) => new()
    {
        Enabled = true,
        OtlpEndpoint = endpoint
    };
}
