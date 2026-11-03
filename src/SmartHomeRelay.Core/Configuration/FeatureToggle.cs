using System.Text.Json.Serialization;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>Settings for a feature whose only option is on/off.</summary>
public class FeatureToggle
{
    /// <summary>Whether the feature runs and its entities appear in Home Assistant. Written first in settings.json.</summary>
    [JsonPropertyOrder(-1)]
    public bool Enabled { get; set; } = true;
}
