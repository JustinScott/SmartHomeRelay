using Microsoft.Extensions.Options;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Configuration;

public sealed class DeviceDefaultsPostConfigureTests
{
    private readonly DeviceDefaultsPostConfigure _postConfigure = new(new FakeHostNameProvider("DESKTOP-ABC123"));

    [Fact]
    public void PostConfigure_EmptyIdAndName_UsesHostNameExactly()
    {
        var settings = new AppSettings();

        _postConfigure.PostConfigure(Options.DefaultName, settings);

        // Same case as Dns.GetHostName() so prototype topics (shr/DESKTOP-ABC123/...) keep working.
        settings.Device.Id.ShouldBe("DESKTOP-ABC123");
        settings.Device.Name.ShouldBe("DESKTOP-ABC123");
    }

    [Fact]
    public void PostConfigure_ExplicitIdAndName_LeavesThemUnchanged()
    {
        var settings = new AppSettings();
        settings.Device.Id = "desktop_main";
        settings.Device.Name = "Main Desktop";

        _postConfigure.PostConfigure(Options.DefaultName, settings);

        settings.Device.Id.ShouldBe("desktop_main");
        settings.Device.Name.ShouldBe("Main Desktop");
    }

    [Fact]
    public void PostConfigure_WhitespaceId_IsTreatedAsEmpty()
    {
        var settings = new AppSettings();
        settings.Device.Id = "  ";

        _postConfigure.PostConfigure(Options.DefaultName, settings);

        settings.Device.Id.ShouldBe("DESKTOP-ABC123");
    }
}
