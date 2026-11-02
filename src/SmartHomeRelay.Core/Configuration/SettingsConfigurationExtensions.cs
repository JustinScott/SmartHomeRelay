using Microsoft.Extensions.Configuration;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>Adds <c>settings.json</c> as a configuration source.</summary>
public static class SettingsConfigurationExtensions
{
    /// <summary>
    /// Adds the settings file: optional (first run), reloaded on change, and tolerant of malformed
    /// JSON. A parse failure yields empty data (defaults) instead of throwing; <see cref="SettingsState"/>
    /// reports the error.
    /// </summary>
    public static IConfigurationBuilder AddRelaySettingsFile(this IConfigurationBuilder builder, string settingsFilePath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);

        var fullPath = Path.GetFullPath(settingsFilePath);

        // The file watcher needs the folder to exist.
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        return builder.AddJsonFile(source =>
        {
            source.Path = fullPath;
            source.Optional = true;
            source.ReloadOnChange = true;
            source.OnLoadException = context => context.Ignore = true;
            source.ResolveFileProvider();
        });
    }
}
