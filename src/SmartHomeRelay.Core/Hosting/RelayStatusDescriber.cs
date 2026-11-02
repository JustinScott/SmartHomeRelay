using SmartHomeRelay.Core.Configuration;

namespace SmartHomeRelay.Core.Hosting;

/// <summary>Derives the tray status and its text, so the tray itself holds no logic.</summary>
public static class RelayStatusDescriber
{
    /// <summary>Status implied by the settings alone (before any connection attempt).</summary>
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
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown status."),
    };
}
