namespace SmartHomeRelay.Core.Abstractions;

/// <summary>A media session's playback status, as Windows reports it (spec 5.9).</summary>
public enum MediaPlaybackStatus
{
    /// <summary>The session is closed.</summary>
    Closed,

    /// <summary>Media is open but hasn't started.</summary>
    Opened,

    /// <summary>Between tracks or states; short-lived.</summary>
    Changing,

    /// <summary>Stopped.</summary>
    Stopped,

    /// <summary>Playing.</summary>
    Playing,

    /// <summary>Paused.</summary>
    Paused,
}
