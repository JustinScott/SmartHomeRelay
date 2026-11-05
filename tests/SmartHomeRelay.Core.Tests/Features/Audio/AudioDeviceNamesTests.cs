using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Features.Audio;

namespace SmartHomeRelay.Core.Tests.Features.Audio;

public sealed class AudioDeviceNamesTests
{
    [Fact]
    public void Build_KeepsNamesAndOrder()
    {
        var names = AudioDeviceNames.Build([new("a", "Speakers"), new("b", "Headset")]);

        names.ShouldBe([("Speakers", "a"), ("Headset", "b")]);
    }

    [Fact]
    public void Build_SameNames_AreMadeUnique()
    {
        // HA select options must be unique; two identical headsets are common.
        var names = AudioDeviceNames.Build([new("a", "Headset"), new("b", "Headset"), new("c", "Headset (2)")]);

        names.Select(n => n.Option).ShouldBe(["Headset", "Headset (2)", "Headset (2) (2)"]);
        names.Select(n => n.Option).ShouldBeUnique();
    }

    [Fact]
    public void Build_BlankName_GetsAPlaceholder()
    {
        AudioDeviceNames.Build([new AudioDevice("a", "  ")]).Single().Option.ShouldBe("Unnamed device");
    }
}
