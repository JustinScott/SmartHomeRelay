using System.Text.Json.Serialization;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// Hardware sensors read from a user-managed LibreHardwareMonitor (spec 5.8). Off by default: use either
/// this feature or Home Assistant's own LibreHardwareMonitor integration, never both.
/// </summary>
public sealed class HardwareSettings : FeatureToggle
{
    /// <summary>LibreHardwareMonitor's default web server port.</summary>
    public const int DefaultPort = 8085;

    /// <summary>Sensors offered when <see cref="Sensors"/> is not set.</summary>
    public static readonly IReadOnlyList<string> DefaultSensors = ["cpu_temperature", "cpu_fan", "ssd_temperature"];

    /// <summary>Creates the settings, off by default.</summary>
    public HardwareSettings()
    {
        Enabled = false;
    }

    /// <summary>LibreHardwareMonitor's web server port. The host is always 127.0.0.1 and is not a setting.</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>LibreHardwareMonitor's web server user name.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>The web server password encrypted with DPAPI (CurrentUser scope), base64 encoded.</summary>
    public string PasswordProtected { get; set; } = string.Empty;

    /// <summary>A password typed into settings.json by hand; encrypted on sight like the MQTT password. Never log this.</summary>
    [JsonIgnore]
    public string Password { get; set; } = string.Empty;

    /// <summary>How often LibreHardwareMonitor is read.</summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Which sensors become entities (keys from <c>HardwareKeys</c>). Null or empty means
    /// <see cref="DefaultSensors"/>; the saved file always spells the list out. Null rather than a filled
    /// list by default because the configuration binder appends to a list that already has items.
    /// Read at startup: a change applies after a restart.
    /// </summary>
    public List<string>? Sensors { get; set; }

    /// <summary>
    /// Optional: a LibreHardwareMonitor sensor ID per key (e.g. <c>"cpu_fan": "/lpc/nct6798d/0/fan/1"</c>)
    /// for when the built-in rule picks the wrong sensor or none.
    /// </summary>
    public Dictionary<string, string> SensorOverrides { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The sensors to offer: <see cref="Sensors"/>, or the defaults when it is empty.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> EffectiveSensors => Sensors is { Count: > 0 } sensors ? sensors : DefaultSensors;
}
