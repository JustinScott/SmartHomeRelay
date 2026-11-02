using System.ComponentModel;
using System.Diagnostics;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Platform.Windows.Hosting;

/// <summary>Opens files and folders through the Windows shell.</summary>
public sealed class ShellLauncher : IShellLauncher
{
    private const int ErrorNoAssociation = 1155;

    /// <inheritdoc />
    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
        Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    /// <inheritdoc />
    public void OpenFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNoAssociation)
        {
            // No app is associated with .json on this PC (spec 7: fall back to Notepad).
            var startInfo = new ProcessStartInfo { FileName = "notepad.exe", UseShellExecute = false };
            startInfo.ArgumentList.Add(path);
            Start(startInfo);
        }
    }

    private static void Start(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
    }
}
