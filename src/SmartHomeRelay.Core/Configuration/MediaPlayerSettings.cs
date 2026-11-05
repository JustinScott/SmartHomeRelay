namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// The PC as a Home Assistant media player through the MQTT Media Player add-on (spec 5.9). Off by default:
/// it needs the add-on installed in Home Assistant.
/// </summary>
public sealed class MediaPlayerSettings : FeatureToggle
{
    /// <summary>Lower the other apps while an announcement plays.</summary>
    public const string InterruptionDuck = "duck";

    /// <summary>Pause the media session while an announcement plays, then resume it.</summary>
    public const string InterruptionPause = "pause";

    /// <summary>The default download cap: 20 MB.</summary>
    public const long DefaultMaxDownloadBytes = 20 * 1024 * 1024;

    /// <summary>Creates the settings, off by default.</summary>
    public MediaPlayerSettings()
    {
        Enabled = false;
    }

    /// <summary>What happens to other audio during an announcement: <c>duck</c> or <c>pause</c>.</summary>
    public string Interruption { get; set; } = InterruptionDuck;

    /// <summary>Other apps' volume while ducked, as a percentage of their own volume.</summary>
    public int DuckLevelPercent { get; set; } = 20;

    /// <summary>Largest audio file that will be downloaded.</summary>
    public long MaxDownloadBytes { get; set; } = DefaultMaxDownloadBytes;

    /// <summary>Timeout for downloading the audio.</summary>
    public int DownloadTimeoutSeconds { get; set; } = 15;

    /// <summary>How often the playback position is published while playing.</summary>
    public int PositionIntervalSeconds { get; set; } = 5;

    /// <summary>Show a silent "Home Assistant is speaking" toast with a Stop button while HA's audio plays (spec 5.6).</summary>
    public bool ShowNotification { get; set; } = true;
}
