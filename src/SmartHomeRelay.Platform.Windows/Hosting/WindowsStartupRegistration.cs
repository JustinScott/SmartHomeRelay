using Microsoft.Win32;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Platform.Windows.Hosting;

/// <summary>
/// "Start with Windows" as the per-user Run value <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SmartHomeRelay</c>
/// (spec 7.1, 8), pointing at the running exe. Only changed when the app runs from its installed folder
/// (<c>%LOCALAPPDATA%\Programs\SmartHomeRelay</c>), so a development build never takes over sign-in.
/// </summary>
public sealed class WindowsStartupRegistration : IStartupRegistration
{
    /// <summary>The Run key.</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The value name, also used by the installer to remove it on uninstall.</summary>
    public const string ValueName = "SmartHomeRelay";

    private readonly string _exePath;
    private readonly string _installedDirectory;
    private readonly string _keyPath;

    /// <summary>Creates the registration for the running exe.</summary>
    public WindowsStartupRegistration()
        : this(Environment.ProcessPath ?? string.Empty, InstalledDirectory, RunKeyPath)
    {
    }

    internal WindowsStartupRegistration(string exePath, string installedDirectory, string keyPath)
    {
        _exePath = exePath;
        _installedDirectory = installedDirectory;
        _keyPath = keyPath;
    }

    /// <summary>Where the installer puts the app.</summary>
    public static string InstalledDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SmartHomeRelay");

    /// <inheritdoc />
    public bool IsInstalled =>
        _exePath.Length > 0
        && string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(_exePath) ?? string.Empty)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(_installedDirectory)),
            StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool Apply(bool startWithWindows)
    {
        if (!IsInstalled)
        {
            return false;
        }

        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        var expected = $"\"{_exePath}\"";
        var current = key.GetValue(ValueName) as string;
        if (startWithWindows)
        {
            if (string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            key.SetValue(ValueName, expected, RegistryValueKind.String);
            return true;
        }

        if (current is null)
        {
            return false;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
        return true;
    }
}
