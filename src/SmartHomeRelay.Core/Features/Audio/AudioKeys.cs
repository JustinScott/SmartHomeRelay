namespace SmartHomeRelay.Core.Features.Audio;

/// <summary>Audio entity keys (spec 5.4). Shipped keys are permanent: never rename them.</summary>
public static class AudioKeys
{
    /// <summary>number (0–100, slider): default output device volume.</summary>
    public const string Volume = "volume";

    /// <summary>switch: default output device mute.</summary>
    public const string Muted = "muted";

    /// <summary>switch: default microphone mute.</summary>
    public const string MicMuted = "mic_muted";

    /// <summary>select: the default playback device.</summary>
    public const string OutputDevice = "output_device";
}
