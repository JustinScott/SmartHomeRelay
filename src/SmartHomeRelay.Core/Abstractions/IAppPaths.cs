namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Well-known file system locations used by the app (spec 7 and 11).</summary>
public interface IAppPaths
{
    /// <summary>Full path of <c>settings.json</c>, normally under <c>%APPDATA%\SmartHomeRelay</c>.</summary>
    string SettingsFilePath { get; }

    /// <summary>Directory for rolling log files, normally <c>%LOCALAPPDATA%\SmartHomeRelay\logs</c>.</summary>
    string LogsDirectory { get; }
}
