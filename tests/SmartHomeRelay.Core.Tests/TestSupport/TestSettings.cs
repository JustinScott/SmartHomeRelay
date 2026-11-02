using SmartHomeRelay.Core.Configuration;

namespace SmartHomeRelay.Core.Tests.TestSupport;

internal static class TestSettings
{
    /// <summary>Defaults plus the values the host-name post-configure would fill in: a valid baseline.</summary>
    public static AppSettings Valid()
    {
        var settings = new AppSettings();
        settings.Device.Id = "TEST-PC";
        settings.Device.Name = "Test PC";
        return settings;
    }
}
