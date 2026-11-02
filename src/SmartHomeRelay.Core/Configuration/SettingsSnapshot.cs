namespace SmartHomeRelay.Core.Configuration;

/// <summary>The settings in effect plus any problems found while loading them.</summary>
/// <param name="Settings">
/// The bound settings. When the file cannot be parsed or bound these are the defaults, so the app
/// keeps running. Consumers that act on settings (e.g. connecting to the broker) should check
/// <see cref="IsValid"/> first.
/// </param>
/// <param name="Errors">Readable problems, empty when the settings are valid.</param>
public sealed record SettingsSnapshot(AppSettings Settings, IReadOnlyList<string> Errors)
{
    /// <summary>True when no problems were found.</summary>
    public bool IsValid => Errors.Count == 0;
}
