namespace SmartHomeRelay.Core.Features.MediaPlayer;

/// <summary>
/// Topic names of the media player (spec 5.9). A compatibility contract once shipped: never rename.
/// </summary>
public static class MediaPlayerKeys
{
    /// <summary>The custom component's name and topic level.</summary>
    public const string Component = "media_player";

    /// <summary>The prefix the MQTT Media Player add-on listens on; fixed, not <c>mqtt.discoveryPrefix</c>.</summary>
    public const string AddOnDiscoveryPrefix = "homeassistant";

    /// <summary>State: <c>playing</c>, <c>paused</c>, <c>idle</c>, or <c>off</c>.</summary>
    public const string State = "state";

    /// <summary>State: track title.</summary>
    public const string Title = "title";

    /// <summary>State: artist.</summary>
    public const string Artist = "artist";

    /// <summary>State: album.</summary>
    public const string Album = "album";

    /// <summary>State: track length in whole seconds.</summary>
    public const string Duration = "duration";

    /// <summary>State: position in whole seconds.</summary>
    public const string Position = "position";

    /// <summary>State: volume, 0.00 to 1.00.</summary>
    public const string Volume = "volume";

    /// <summary>State: <c>mute</c> or <c>unmute</c>.</summary>
    public const string Mute = "mute";

    /// <summary>State: <c>music</c>, <c>video</c>, or <c>image</c>.</summary>
    public const string MediaType = "media_type";

    /// <summary>Command: play.</summary>
    public const string Play = "play";

    /// <summary>Command: pause.</summary>
    public const string Pause = "pause";

    /// <summary>Command: play/pause.</summary>
    public const string PlayPause = "playpause";

    /// <summary>Command: next track.</summary>
    public const string Next = "next";

    /// <summary>Command: previous track.</summary>
    public const string Previous = "previous";

    /// <summary>Command: seek, in seconds.</summary>
    public const string Seek = "seek";

    /// <summary>Command: play the URL HA sends (JSON).</summary>
    public const string PlayMedia = "play_media";

    /// <summary>Every state, in publishing order.</summary>
    public static readonly IReadOnlyList<string> States = [State, Title, Artist, Album, Duration, Position, Volume, Mute, MediaType];

    /// <summary>Every command.</summary>
    public static readonly IReadOnlyList<string> Commands = [Play, Pause, PlayPause, Next, Previous, Volume, Mute, Seek, PlayMedia];

    /// <summary>The add-on's discovery topic for a device.</summary>
    public static string DiscoveryTopic(string deviceId) => $"{AddOnDiscoveryPrefix}/{Component}/{deviceId}/config";
}
