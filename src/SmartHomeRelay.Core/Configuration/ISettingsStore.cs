namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// Writes <c>settings.json</c>. Reading goes through the Options pattern
/// (<see cref="SettingsConfigurationExtensions.AddRelaySettingsFile"/>), so changes are picked up live.
/// </summary>
public interface ISettingsStore
{
    /// <summary>True if the settings file exists.</summary>
    bool Exists();

    /// <summary>Writes <paramref name="settings"/> atomically, creating the folder if needed.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a readable description of why the file cannot be parsed as JSON,
    /// or null if it is missing or parses correctly.
    /// </summary>
    string? GetSyntaxError();
}
