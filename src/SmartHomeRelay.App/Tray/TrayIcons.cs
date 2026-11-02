using SmartHomeRelay.Core.Hosting;

namespace SmartHomeRelay.App.Tray;

/// <summary>Loads tray icons embedded in the assembly.</summary>
internal static class TrayIcons
{
    /// <summary>Returns a new icon for <paramref name="severity"/>, sized for the tray; the caller disposes it.</summary>
    public static Icon Load(StatusSeverity severity)
    {
        var name = severity switch
        {
            StatusSeverity.Warning => "SmartHomeRelayWarning.ico",
            StatusSeverity.Error => "SmartHomeRelayError.ico",
            _ => "SmartHomeRelay.ico",
        };

        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded icon '{name}' is missing.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
