namespace SmartHomeRelay.Core.Abstractions;

/// <summary>
/// The PC's default audio devices (spec 5.4): read and change volume and mute, the default microphone's
/// mute, and which playback device is the default. Standard user; no elevation.
/// </summary>
public interface IAudioController
{
    /// <summary>
    /// Raised when anything in <see cref="AudioState"/> may have changed (volume, mute, devices added or
    /// removed, default device switched). May be raised on any thread; handlers must not block.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>Reads the current state.</summary>
    Task<AudioState> GetStateAsync(CancellationToken cancellationToken);

    /// <summary>Sets the default output device's master volume (0 to 1).</summary>
    Task SetVolumeAsync(double level, CancellationToken cancellationToken);

    /// <summary>Mutes or unmutes the default output device.</summary>
    Task SetMutedAsync(bool muted, CancellationToken cancellationToken);

    /// <summary>Mutes or unmutes the default microphone.</summary>
    Task SetMicMutedAsync(bool muted, CancellationToken cancellationToken);

    /// <summary>Makes <paramref name="deviceId"/> the default playback device (for all roles).</summary>
    Task SetDefaultOutputAsync(string deviceId, CancellationToken cancellationToken);
}
