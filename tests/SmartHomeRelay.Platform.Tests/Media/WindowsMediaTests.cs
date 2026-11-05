using SmartHomeRelay.Platform.Windows.Media;

namespace SmartHomeRelay.Platform.Tests.Media;

/// <summary>
/// The real Windows media APIs (spec 5.9). Read-only, a silent clip, and a duck to 100 %, so nothing audible
/// changes (spec 9). CI VMs may have no audio device and no media session.
/// </summary>
[Trait("Category", "Windows")]
public sealed class WindowsMediaTests : IDisposable
{
    private readonly WindowsMediaSessionSource _session = new();
    private readonly string _duckFile = Path.Combine(Path.GetTempPath(), $"shr-duck-{Guid.NewGuid():N}.json");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _session.Dispose();
        File.Delete(_duckFile);
    }

    [Fact]
    public async Task MediaSession_ReadsWithoutThrowing()
    {
        var info = await _session.GetCurrentAsync(Ct);

        if (info is not null)
        {
            info.Position?.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task Ducker_FullLevel_DucksAndRestoresWithoutChangingAnything()
    {
        var ducker = new WindowsAudioDucker(_duckFile);

        await ducker.DuckAsync(1.0, Ct);
        File.Exists(_duckFile).ShouldBeTrue("saved before any change");
        await ducker.RestoreAsync(Ct);

        File.Exists(_duckFile).ShouldBeFalse();
    }

    [Fact]
    public async Task Ducker_RecoverWithNothingSaved_DoesNothing()
    {
        await new WindowsAudioDucker(_duckFile).RecoverAsync(Ct);

        File.Exists(_duckFile).ShouldBeFalse();
    }

    [Fact]
    public async Task Player_PlaysASilentClip_ToTheEnd()
    {
        var player = new WindowsAnnouncementPlayer();

        try
        {
            await player.PlayAsync(SilentWav(TimeSpan.FromMilliseconds(300)), "audio/wav", Ct).WaitAsync(TimeSpan.FromSeconds(20), Ct);
        }
        catch (InvalidOperationException) when (Environment.GetEnvironmentVariable("CI") is not null)
        {
            Assert.Skip("No audio device on the CI machine.");
        }
    }

    [Fact]
    public async Task Player_GarbageAudio_Throws()
    {
        var player = new WindowsAnnouncementPlayer();

        await Should.ThrowAsync<Exception>(() => player.PlayAsync([1, 2, 3, 4], "audio/mpeg", Ct));
    }

    /// <summary>16-bit mono 8 kHz PCM silence.</summary>
    private static byte[] SilentWav(TimeSpan length)
    {
        const int rate = 8000;
        var samples = (int)(rate * length.TotalSeconds);
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(36 + (samples * 2));
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples * 2);
        w.Write(new byte[samples * 2]);
        w.Flush();
        return stream.ToArray();
    }
}
