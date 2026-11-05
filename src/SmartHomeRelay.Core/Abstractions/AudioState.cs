namespace SmartHomeRelay.Core.Abstractions;

/// <summary>A snapshot of the PC's audio (spec 5.4). Null values mean there is no such device.</summary>
/// <param name="Volume">Master volume of the default output device, 0 to 1.</param>
/// <param name="Muted">Whether the default output device is muted.</param>
/// <param name="MicMuted">Whether the default microphone is muted.</param>
/// <param name="OutputDevices">Active playback devices, in Windows' order.</param>
/// <param name="DefaultOutputId">The default playback device's ID.</param>
public sealed record AudioState(
    double? Volume,
    bool? Muted,
    bool? MicMuted,
    IReadOnlyList<AudioDevice> OutputDevices,
    string? DefaultOutputId);
