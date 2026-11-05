using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Platform.Windows.Configuration;

/// <summary>Settings under <c>%APPDATA%</c> (roams, spec 7); logs under <c>%LOCALAPPDATA%</c> (spec 11).</summary>
public sealed class WindowsAppPaths : IAppPaths
{
    private const string AppFolderName = "SmartHomeRelay";

    /// <inheritdoc />
    public string SettingsFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName,
        "settings.json");

    /// <inheritdoc />
    public string LogsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName,
        "logs");

    /// <inheritdoc />
    public string CacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName,
        "cache");
}
