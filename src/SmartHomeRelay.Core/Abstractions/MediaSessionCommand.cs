namespace SmartHomeRelay.Core.Abstractions;

/// <summary>A transport command for the current media session (spec 5.9).</summary>
public enum MediaSessionCommand
{
    /// <summary>Play.</summary>
    Play,

    /// <summary>Pause.</summary>
    Pause,

    /// <summary>Play if paused, pause if playing.</summary>
    TogglePlayPause,

    /// <summary>Next track.</summary>
    Next,

    /// <summary>Previous track.</summary>
    Previous,
}
