using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.UnitTests;

public sealed class HealthAggregatorTests
{
    [Fact]
    public void RequiredCriticalProbeMakesComponentCritical()
    {
        var result = HealthAggregator.Aggregate(new[]
        {
            (HealthStatus.Healthy, true),
            (HealthStatus.Critical, true),
            (HealthStatus.Warning, false)
        });

        Assert.Equal(HealthStatus.Critical, result);
    }

    [Fact]
    public void OptionalCriticalProbeMakesComponentWarning()
    {
        var result = HealthAggregator.Aggregate(new[]
        {
            (HealthStatus.Healthy, true),
            (HealthStatus.Critical, false)
        });

        Assert.Equal(HealthStatus.Warning, result);
    }

    [Fact]
    public void NoEvidenceIsUnknown()
    {
        Assert.Equal(HealthStatus.Unknown, HealthAggregator.Aggregate(Array.Empty<(HealthStatus, bool)>()));
    }
}
