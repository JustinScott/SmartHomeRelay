namespace SmartHomeRelay.Core.Configuration;

/// <summary>What the settings window edits (spec 7.1).</summary>
/// <param name="Settings">A copy of the settings in effect, safe to change.</param>
/// <param name="FileProblems">Problems with settings.json right now; saving replaces the file with valid settings.</param>
/// <param name="HasMqttPassword">A broker password is saved.</param>
/// <param name="HasHardwarePassword">A LibreHardwareMonitor password is saved.</param>
public sealed record SettingsDraft(AppSettings Settings, IReadOnlyList<string> FileProblems, bool HasMqttPassword, bool HasHardwarePassword);
