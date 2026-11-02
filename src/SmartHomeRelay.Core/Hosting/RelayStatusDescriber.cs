using SmartHomeRelay.Core.Configuration;

namespace SmartHomeRelay.Core.Hosting;

/// <summary>Derives the tray status, text, and severity, so the tray itself holds no logic.</summary>
public static class RelayStatusDescriber
{
    /// <summary>
    /// Status implied by the settings alone: <see cref="RelayStatus.Disconnected"/> means "ready to connect".
    /// </summary>
    public static RelayStatus FromSettings(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.IsValid)
        {
            return RelayStatus.SettingsInvalid;
        }

        return snapshot.Settings.Mqtt.IsConfigured ? RelayStatus.Disconnected : RelayStatus.NotConfigured;
    }

    /// <summary>Short text for the tray menu and tooltip.</summary>
    public static string Describe(RelayStatus status) => status switch
    {
        RelayStatus.NotConfigured => "Not configured",
        RelayStatus.SettingsInvalid => "Settings error (see log)",
        RelayStatus.Disconnected => "Not connected",
        RelayStatus.Connecting => "Connecting",
        RelayStatus.Connected => "Connected",
        RelayStatus.Reconnecting => "Reconnecting",
        RelayStatus.Error => "Connection error (see log)",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown status."),
    };

    /// <summary>Which tray icon to show.</summary>
    public static StatusSeverity Severity(RelayStatus status) => status switch
    {
        RelayStatus.Connected => StatusSeverity.Ok,
        RelayStatus.SettingsInvalid or RelayStatus.Error => StatusSeverity.Error,
        _ => StatusSeverity.Warning,
    };
}
