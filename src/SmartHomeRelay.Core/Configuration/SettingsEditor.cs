using System.Text.Json;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// The settings window's logic (spec 7.1), kept out of the form so it is testable: hands out an editable copy,
/// encrypts newly typed passwords (an empty box keeps the saved one), validates, and saves. The running app picks
/// the saved file up like any other change.
/// </summary>
public sealed class SettingsEditor
{
    private readonly ISettingsState _state;
    private readonly ISettingsStore _store;
    private readonly ISecretProtector _protector;
    private readonly SettingsValidator _validator;

    /// <summary>Creates the editor.</summary>
    public SettingsEditor(ISettingsState state, ISettingsStore store, ISecretProtector protector, SettingsValidator validator)
    {
        _state = state;
        _store = store;
        _protector = protector;
        _validator = validator;
    }

    /// <summary>A copy of the settings in effect to edit.</summary>
    public SettingsDraft Load()
    {
        var snapshot = _state.Current;
        var copy = Clone(snapshot.Settings);
        return new SettingsDraft(
            copy,
            snapshot.Errors,
            HasMqttPassword: copy.Mqtt.PasswordProtected.Length > 0 || snapshot.Settings.Mqtt.Password.Length > 0,
            HasHardwarePassword: copy.Features.Hardware.PasswordProtected.Length > 0 || snapshot.Settings.Features.Hardware.Password.Length > 0);
    }

    /// <summary>
    /// Validates and saves <paramref name="settings"/>. A non-empty <paramref name="newMqttPassword"/> or
    /// <paramref name="newHardwarePassword"/> replaces the saved one, encrypted; never written in plain text.
    /// Returns the problems found; empty means saved.
    /// </summary>
    public async Task<IReadOnlyList<string>> SaveAsync(AppSettings settings, string? newMqttPassword, string? newHardwarePassword, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var copy = Clone(settings);
        copy.Mqtt.Host = copy.Mqtt.Host.Trim();
        copy.Device.Id = copy.Device.Id.Trim();
        copy.Device.Name = copy.Device.Name.Trim();
        if (!string.IsNullOrEmpty(newMqttPassword))
        {
            copy.Mqtt.PasswordProtected = _protector.Protect(newMqttPassword);
        }

        if (!string.IsNullOrEmpty(newHardwarePassword))
        {
            copy.Features.Hardware.PasswordProtected = _protector.Protect(newHardwarePassword);
        }

        var errors = _validator.Validate(copy);
        if (errors.Count > 0)
        {
            return errors;
        }

        await _store.SaveAsync(copy, cancellationToken).ConfigureAwait(false);
        return [];
    }

    /// <summary>A deep copy (plain-text passwords, which are never serialized, are left out).</summary>
    public static AppSettings Clone(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, SettingsJson.Options), SettingsJson.Options)
        ?? throw new InvalidOperationException("Settings could not be copied.");
}
