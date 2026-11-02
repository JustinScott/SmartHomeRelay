using SmartHomeRelay.Platform.Windows.Configuration;

namespace SmartHomeRelay.Platform.Tests.Configuration;

[Trait("Category", "Windows")]
public sealed class WindowsAppPathsTests
{
    [Fact]
    public void SettingsFilePath_IsUnderRoamingAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmartHomeRelay",
            "settings.json");

        new WindowsAppPaths().SettingsFilePath.ShouldBe(expected);
    }

    [Fact]
    public void LogsDirectory_IsUnderLocalAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartHomeRelay",
            "logs");

        new WindowsAppPaths().LogsDirectory.ShouldBe(expected);
    }
}
