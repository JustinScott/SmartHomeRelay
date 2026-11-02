using System.Diagnostics;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Platform.Windows.Hosting;

/// <summary>Opens a folder in File Explorer via the shell.</summary>
public sealed class ShellFolderOpener : IFolderOpener
{
    /// <inheritdoc />
    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}
