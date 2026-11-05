using SmartHomeRelay.Platform.Windows.Audio;

namespace SmartHomeRelay.Platform.Tests.Audio;

/// <summary>
/// The real Windows audio stack. Read-only, plus setting the volume and mute to the values they already have,
/// so the PC's audio is never actually changed (spec 9: no destructive actions). CI VMs may have no devices.
/// </summary>
[Trait("Category", "Windows")]
public sealed class WindowsAudioControllerTests : IDisposable
{
    private readonly WindowsAudioController _audio = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _audio.Dispose();

    [Fact]
    public async Task GetState_ReadsDevicesAndLevels()
    {
        var state = await _audio.GetStateAsync(Ct);

        state.OutputDevices.ShouldAllBe(d => !string.IsNullOrEmpty(d.Id) && d.Name != null);
        state.OutputDevices.Select(d => d.Id).ShouldBeUnique();
        if (state.DefaultOutputId is { } id)
        {
            state.OutputDevices.ShouldContain(d => d.Id == id);
            state.Volume.ShouldNotBeNull().ShouldBeInRange(0, 1);
            state.Muted.ShouldNotBeNull();
        }
        else
        {
            state.Volume.ShouldBeNull();
        }
    }

    [Fact]
    public async Task SettingTheCurrentValues_RoundTrips()
    {
        var state = await _audio.GetStateAsync(Ct);
        if (state.Volume is not { } volume || state.Muted is not { } muted)
        {
            return; // No playback device on this machine.
        }

        await _audio.SetVolumeAsync(volume, Ct);
        await _audio.SetMutedAsync(muted, Ct);

        var after = await _audio.GetStateAsync(Ct);
        after.Volume.ShouldNotBeNull().ShouldBe(volume, tolerance: 0.001);
        after.Muted.ShouldBe(muted);
    }

    [Fact]
    public async Task SetDefaultOutput_ToTheCurrentDefault_Works()
    {
        // Proves the undocumented IPolicyConfig vtable on this Windows build without changing anything.
        var state = await _audio.GetStateAsync(Ct);
        if (state.DefaultOutputId is not { } id)
        {
            return;
        }

        await _audio.SetDefaultOutputAsync(id, Ct);

        (await _audio.GetStateAsync(Ct)).DefaultOutputId.ShouldBe(id);
    }
}
