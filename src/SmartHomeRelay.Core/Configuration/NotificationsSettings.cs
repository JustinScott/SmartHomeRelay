namespace SmartHomeRelay.Core.Configuration;

/// <summary>Notifications feature settings (spec 5.6).</summary>
public sealed class NotificationsSettings : FeatureToggle
{
    /// <summary>The default image size cap: 10 MB.</summary>
    public const long DefaultImageMaxBytes = 10 * 1024 * 1024;

    /// <summary>Timeout for downloading a hero image.</summary>
    public int ImageDownloadTimeoutSeconds { get; set; } = 10;

    /// <summary>Largest hero image that will be downloaded.</summary>
    public long ImageMaxBytes { get; set; } = DefaultImageMaxBytes;
}
