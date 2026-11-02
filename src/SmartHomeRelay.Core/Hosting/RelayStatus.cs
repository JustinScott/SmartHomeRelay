namespace SmartHomeRelay.Core.Hosting;

/// <summary>Overall state shown in the tray (spec 11). Connection states are added with the MQTT core.</summary>
public enum RelayStatus
{
    /// <summary>No broker host is set.</summary>
    NotConfigured,

    /// <summary>The settings file has errors; see the log.</summary>
    SettingsInvalid,

    /// <summary>Configured but not connected to the broker.</summary>
    Disconnected,
}
