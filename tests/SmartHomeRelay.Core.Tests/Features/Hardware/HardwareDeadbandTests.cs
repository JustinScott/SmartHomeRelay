using SmartHomeRelay.Core.Features.Hardware;

namespace SmartHomeRelay.Core.Tests.Features.Hardware;

public sealed class HardwareDeadbandTests
{
    private static readonly HardwareSensorDefinition Temperature = HardwareKeys.Get(HardwareKeys.CpuTemperature);
    private static readonly HardwareSensorDefinition Fan = HardwareKeys.Get(HardwareKeys.CpuFan);

    [Theory]
    [InlineData(47, 48.9, false)]
    [InlineData(47, 49, true)]
    [InlineData(47, 45, true)]
    public void Temperature_TwoDegrees(double last, double current, bool expected)
    {
        HardwareDeadband.ShouldPublish(Temperature, last, current).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1250, 1299, false)] // under 5% (62.5 RPM)
    [InlineData(1250, 1313, true)] // 5% of 1250 = 62.5
    [InlineData(2000, 2090, false)]
    [InlineData(2000, 2100, true)]
    [InlineData(800, 0, true)] // fan stopped
    public void Fan_FivePercentWithFiftyRpmFloor(double last, double current, bool expected)
    {
        HardwareDeadband.ShouldPublish(Fan, last, current).ShouldBe(expected);
    }

    [Fact]
    public void FirstValue_AlwaysPublishes()
    {
        HardwareDeadband.ShouldPublish(Fan, null, 0).ShouldBeTrue();
    }

    [Theory]
    [InlineData(47.3, "47")]
    [InlineData(47.5, "48")]
    [InlineData(-3.6, "-4")]
    public void Format_IsWholeNumbers(double value, string expected)
    {
        HardwareDeadband.Format(value).ShouldBe(expected);
    }
}
