namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Well-known file system locations used by the app (spec 7 and 11).</summary>
public interface IAppPaths
{
    /// <summary>Full path of <c>settings.json</c>, normally under <c>%APPDATA%\SmartHomeRelay</c>.</summary>
    string SettingsFilePath { get; }

    /// <summary>Directory for rolling log files, normally <c>%LOCALAPPDATA%\SmartHomeRelay\logs</c>.</summary>
    string LogsDirectory { get; }

    /// <summary>
    /// Directory for files the app may delete at any time (e.g. downloaded notification images), normally
    /// <c>%LOCALAPPDATA%\SmartHomeRelay\cache</c>.
    /// </summary>
    string CacheDirectory { get; }
}
