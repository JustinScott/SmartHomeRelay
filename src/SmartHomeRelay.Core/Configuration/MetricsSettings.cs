namespace SmartHomeRelay.Core.Configuration;

/// <summary>System metrics feature settings (spec 5.5).</summary>
public sealed class MetricsSettings : FeatureToggle
{
    /// <summary>How often metrics are sampled.</summary>
    public int IntervalSeconds { get; set; } = 30;
}
