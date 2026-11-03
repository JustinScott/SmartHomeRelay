using SmartHomeRelay.Core.Features.Hardware;

namespace SmartHomeRelay.Core.Tests.Features.Hardware;

public sealed class LhmCompatibilityTests
{
    [Theory]
    [InlineData("0.9.4.0", LhmVersionSupport.TooOld)]
    [InlineData("0.9.5.0", LhmVersionSupport.Tested)]
    [InlineData("0.9.6.0", LhmVersionSupport.Tested)]
    [InlineData("0.9.6.42", LhmVersionSupport.Tested)] // revision ignored
    [InlineData("0.9.7.0", LhmVersionSupport.Newer)]
    [InlineData("1.0", LhmVersionSupport.Newer)]
    public void Classify_ComparesMajorMinorBuild(string version, LhmVersionSupport expected)
    {
        LhmCompatibility.Classify(Version.Parse(version)).ShouldBe(expected);
    }

    [Fact]
    public void Format_IsThreeParts()
    {
        LhmCompatibility.Format(new Version(0, 9, 6, 0)).ShouldBe("0.9.6");
        LhmCompatibility.Format(new Version(1, 0)).ShouldBe("1.0.0");
    }
}
