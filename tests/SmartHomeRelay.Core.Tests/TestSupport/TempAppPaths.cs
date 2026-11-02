using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>App paths inside a unique temp folder that is deleted on dispose.</summary>
internal sealed class TempAppPaths : IAppPaths, IDisposable
{
    public TempAppPaths()
    {
        Root = Path.Combine(Path.GetTempPath(), "shr-tests", Guid.NewGuid().ToString("N"));
        SettingsFilePath = Path.Combine(Root, "config", "settings.json");
        LogsDirectory = Path.Combine(Root, "logs");
    }

    public string Root { get; }

    public string SettingsFilePath { get; }

    public string LogsDirectory { get; }

    public void WriteSettingsFile(string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
        File.WriteAllText(SettingsFilePath, contents);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
