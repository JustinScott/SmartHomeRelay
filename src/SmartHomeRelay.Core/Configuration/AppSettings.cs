using Microsoft.Extensions.Logging;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// Root of <c>%APPDATA%\SmartHomeRelay\settings.json</c> (spec 7).
/// Mutable so it can be bound with the Options pattern; treat instances as read-only after binding.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Broker connection settings.</summary>
    public MqttSettings Mqtt { get; set; } = new();

    /// <summary>Identity of this PC in Home Assistant.</summary>
    public DeviceSettings Device { get; set; } = new();

    /// <summary>Per-feature toggles and options.</summary>
    public FeaturesSettings Features { get; set; } = new();

    /// <summary>Whether the per-user <c>Run</c> registry entry should exist.</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Minimum level written to the log file (spec 11). <c>Debug</c> is for troubleshooting.</summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
}
