using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Service.Monitoring;

namespace ProtheusPulse.UnitTests;

public sealed class AlertRuleConfigurationTests
{
    [Fact]
    public void ReadsTriggerStatusesWrittenInCamelCase()
    {
        var statuses = AlertEngine.ReadTriggerStatuses("{\"triggerStatuses\":[\"Critical\",\"Unknown\"]}");

        Assert.Equal([HealthStatus.Critical, HealthStatus.Unknown], statuses);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"triggerStatuses\":[]}")]
    [InlineData("{\"triggerStatuses\":[\"NaoExiste\"]}")]
    [InlineData("nao é json")]
    public void FallsBackToWarningAndCriticalWhenConfigurationIsEmptyOrBroken(string configurationJson)
    {
        var statuses = AlertEngine.ReadTriggerStatuses(configurationJson);

        Assert.Equal([HealthStatus.Warning, HealthStatus.Critical], statuses);
    }
}
