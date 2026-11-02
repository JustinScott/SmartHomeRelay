namespace SmartHomeRelay.Core.Configuration;

/// <summary>Per-feature settings. Every feature is individually toggleable (spec 5).</summary>
public sealed class FeaturesSettings
{
    /// <summary>Presence feature settings.</summary>
    public PresenceSettings Presence { get; set; } = new();

    /// <summary>Power feature settings.</summary>
    public PowerSettings Power { get; set; } = new();

    /// <summary>Display feature settings.</summary>
    public FeatureToggle Display { get; set; } = new();

    /// <summary>Audio feature settings.</summary>
    public FeatureToggle Audio { get; set; } = new();

    /// <summary>System metrics feature settings.</summary>
    public MetricsSettings Metrics { get; set; } = new();

    /// <summary>Notifications feature settings.</summary>
    public NotificationsSettings Notifications { get; set; } = new();
}
