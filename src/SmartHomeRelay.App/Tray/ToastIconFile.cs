using System.Drawing.Imaging;

namespace SmartHomeRelay.App.Tray;

/// <summary>
/// Writes the app icon as a PNG for toasts (spec 5.6): Windows reads the icon of an unpackaged app's toasts from a
/// file path. Written at every start; on failure toasts simply show no icon.
/// </summary>
internal static class ToastIconFile
{
    /// <summary>Writes the icon into <paramref name="directory"/> and returns its path, or null if that failed.</summary>
    public static string? Write(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "toast-icon.png");
            using var stream = typeof(ToastIconFile).Assembly.GetManifestResourceStream("SmartHomeRelay.ico")
                ?? throw new InvalidOperationException("Embedded icon is missing.");
            using var icon = new Icon(stream, 256, 256);
            using var bitmap = icon.ToBitmap();
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }
}
