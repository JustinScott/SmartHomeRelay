using SmartHomeRelay.Core.Features.Metrics;

namespace SmartHomeRelay.Core.Tests.Features.Metrics;

/// <summary>The "moderate" deadband chosen for spec 5.5.</summary>
public sealed class MetricDeadbandTests
{
    [Theory]
    [InlineData(MetricKind.Percent)]
    [InlineData(MetricKind.Gigabytes)]
    [InlineData(MetricKind.Rate)]
    [InlineData(MetricKind.Watts)]
    [InlineData(MetricKind.Temperature)]
    public void ShouldPublish_FirstValue_AlwaysPublishes(MetricKind kind)
    {
        MetricDeadband.ShouldPublish(kind, null, 0).ShouldBeTrue();
    }

    [Theory]
    [InlineData(40, 41.9, false)]
    [InlineData(40, 42, true)]
    [InlineData(40, 38, true)]
    [InlineData(3, 1, true)]
    [InlineData(1, 0, false)] // 1 point: within the deadband
    public void ShouldPublish_Percent_TwoPoints(double last, double current, bool expected)
    {
        MetricDeadband.ShouldPublish(MetricKind.Percent, last, current).ShouldBe(expected);
    }

    [Theory]
    [InlineData(12.0, 12.05, false)]
    [InlineData(12.0, 12.1, true)]
    [InlineData(12.0, 11.9, true)]
    public void ShouldPublish_Gigabytes_TenthOfAGigabyte(double last, double current, bool expected)
    {
        MetricDeadband.ShouldPublish(MetricKind.Gigabytes, last, current).ShouldBe(expected);
    }

    [Theory]
    [InlineData(100, 109, false)] // under 10%
    [InlineData(100, 110, true)]
    [InlineData(100, 89, true)]
    [InlineData(0, 0.05, false)] // under the 0.1 Mbit/s floor
    [InlineData(0, 0.1, true)]
    [InlineData(0.5, 0, true)] // dropping to zero is published
    public void ShouldPublish_Rate_TenPercentWithFloor(double last, double current, bool expected)
    {
        MetricDeadband.ShouldPublish(MetricKind.Rate, last, current).ShouldBe(expected);
    }

    [Theory]
    [InlineData(10, 11.9, false)] // under the 2 W floor
    [InlineData(10, 12, true)]
    [InlineData(56, 61, false)] // under 10% (5.6 W)
    [InlineData(56, 62, true)]
    [InlineData(56, 40, true)]
    public void ShouldPublish_Watts_TenPercentWithTwoWattFloor(double last, double current, bool expected)
    {
        MetricDeadband.ShouldPublish(MetricKind.Watts, last, current).ShouldBe(expected);
    }

    [Theory]
    [InlineData(47, 48.5, false)]
    [InlineData(47, 49, true)]
    [InlineData(47, 45, true)]
    public void ShouldPublish_Temperature_TwoDegrees(double last, double current, bool expected)
    {
        MetricDeadband.ShouldPublish(MetricKind.Temperature, last, current).ShouldBe(expected);
    }

    [Theory]
    [InlineData(MetricKind.Watts, 56.05, "56")]
    [InlineData(MetricKind.Watts, -3, "0")]
    [InlineData(MetricKind.Temperature, 47.5, "48")]
    [InlineData(MetricKind.Percent, 42.4, "42")]
    [InlineData(MetricKind.Percent, 42.5, "43")]
    [InlineData(MetricKind.Percent, 104, "100")]
    [InlineData(MetricKind.Percent, -1, "0")]
    [InlineData(MetricKind.Gigabytes, 12.345, "12.3")]
    [InlineData(MetricKind.Gigabytes, 7, "7.0")]
    [InlineData(MetricKind.Rate, 0.04, "0.0")]
    [InlineData(MetricKind.Rate, 85.27, "85.3")]
    public void Format_UsesInvariantCulture(MetricKind kind, double value, string expected)
    {
        MetricDeadband.Format(kind, value).ShouldBe(expected);
    }
}
