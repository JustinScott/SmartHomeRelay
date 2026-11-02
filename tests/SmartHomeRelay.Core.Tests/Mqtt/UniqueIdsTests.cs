using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Tests.Mqtt;

/// <summary>The unique_id rule is permanent (spec 4.1); these pin it down.</summary>
public sealed class UniqueIdsTests
{
    [Theory]
    [InlineData("DESKTOP-ABC123", "desktop_abc123")]
    [InlineData("desktop_main", "desktop_main")]
    [InlineData("Gaming-PC-2", "gaming_pc_2")]
    public void SanitizeDeviceId_LowercasesAndReplacesOtherCharacters(string deviceId, string expected)
    {
        UniqueIds.SanitizeDeviceId(deviceId).ShouldBe(expected);
    }

    [Fact]
    public void For_CombinesSanitizedDeviceIdAndKey()
    {
        UniqueIds.For("DESKTOP-ABC123", "cpu_usage").ShouldBe("desktop_abc123_cpu_usage");
    }
}
