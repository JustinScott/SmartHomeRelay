namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// The current, validated settings. Never throws for a bad settings file: problems are reported in
/// <see cref="SettingsSnapshot.Errors"/> instead, so one bad value cannot crash the app (spec 7).
/// </summary>
public interface ISettingsState
{
    /// <summary>Raised after the settings file changed and was re-evaluated. May be raised on any thread.</summary>
    event EventHandler? Changed;

    /// <summary>The latest snapshot. Safe to read from any thread.</summary>
    SettingsSnapshot Current { get; }
}
