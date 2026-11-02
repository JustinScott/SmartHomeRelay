namespace SmartHomeRelay.Core.Configuration;

/// <summary>Presence feature settings (spec 5.1).</summary>
public sealed class PresenceSettings : FeatureToggle
{
    /// <summary>Keyboard/mouse idle time after which the user counts as idle.</summary>
    public int IdleThresholdSeconds { get; set; } = 300;
}
