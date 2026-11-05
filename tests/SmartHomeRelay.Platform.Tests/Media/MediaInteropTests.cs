using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Interop.CoreAudio;
using SmartHomeRelay.Platform.Windows.Media;

namespace SmartHomeRelay.Platform.Tests.Media;

/// <summary>Pure parts of the media player's Windows side (spec 5.9).</summary>
public sealed class MediaInteropTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InterfaceIds_MatchTheWindowsSdk()
    {
        typeof(IAudioSessionManager2).GUID.ShouldBe(new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"));
        typeof(IAudioSessionEnumerator).GUID.ShouldBe(new Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"));
        typeof(IAudioSessionControl2).GUID.ShouldBe(new Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"));
        typeof(ISimpleAudioVolume).GUID.ShouldBe(new Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"));
    }

    [Fact]
    public void Timeline_IsRelativeToTheStart()
    {
        var (duration, position) = WindowsMediaSessionSource.Timeline(
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(205), TimeSpan.FromSeconds(15), s_now, MediaPlaybackStatus.Paused, 1, s_now.AddSeconds(30));

        duration.ShouldBe(TimeSpan.FromSeconds(200));
        position.ShouldBe(TimeSpan.FromSeconds(10), "paused: no advance");
    }

    [Fact]
    public void Timeline_WhilePlaying_AdvancesSinceTheLastReport()
    {
        var (_, position) = WindowsMediaSessionSource.Timeline(
            TimeSpan.Zero, TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(10), s_now, MediaPlaybackStatus.Playing, 1, s_now.AddSeconds(30));

        position.ShouldBe(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public void Timeline_NeverPassesTheEnd()
    {
        var (_, position) = WindowsMediaSessionSource.Timeline(
            TimeSpan.Zero, TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(190), s_now, MediaPlaybackStatus.Playing, 1, s_now.AddMinutes(5));

        position.ShouldBe(TimeSpan.FromSeconds(200));
    }

    [Fact]
    public void Timeline_NoneReported_IsUnknown()
    {
        WindowsMediaSessionSource.Timeline(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.MinValue, MediaPlaybackStatus.Playing, 1, s_now)
            .ShouldBe((null, null));
    }

    [Fact]
    public void DuckedSession_UserChangeWins()
    {
        var saved = new DuckedSession("app", "app#1", 0.8f, 0.16f);

        saved.IsUntouched(0.16f).ShouldBeTrue();
        saved.IsUntouched(0.162f).ShouldBeTrue();
        saved.IsUntouched(0.5f).ShouldBeFalse("the user turned it up during the announcement");
    }

    [Fact]
    public void DuckedVolumesFile_RoundTrips_AndToleratesGarbage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shr-duck-{Guid.NewGuid():N}.json");
        var file = new DuckedVolumesFile(path);
        try
        {
            file.Load().ShouldBeEmpty();
            file.Save([new DuckedSession("a", "a#1", 1f, 0.2f)]);
            file.Load().ShouldBe([new DuckedSession("a", "a#1", 1f, 0.2f)]);

            File.WriteAllText(path, "not json");
            file.Load().ShouldBeEmpty();

            file.Delete();
            File.Exists(path).ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
