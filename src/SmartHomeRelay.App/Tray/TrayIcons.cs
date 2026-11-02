namespace SmartHomeRelay.App.Tray;

/// <summary>Loads tray icons embedded in the assembly.</summary>
internal static class TrayIcons
{
    /// <summary>Returns a new icon sized for the tray; the caller disposes it.</summary>
    public static Icon LoadAppIcon()
    {
        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream("SmartHomeRelay.ico")
            ?? throw new InvalidOperationException("Embedded icon 'SmartHomeRelay.ico' is missing.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
