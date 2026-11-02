using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// Wraps <see cref="IOptionsMonitor{TOptions}"/> so syntax errors, wrong-type values, and invalid
/// values become readable errors instead of exceptions. Re-evaluates whenever the file changes.
/// </summary>
public sealed partial class SettingsState : ISettingsState, IHostedService, IDisposable
{
    private readonly IOptionsMonitor<AppSettings> _options;
    private readonly ISettingsStore _store;
    private readonly SettingsValidator _validator;
    private readonly DeviceDefaultsPostConfigure _defaults;
    private readonly ILogger<SettingsState> _logger;
    private SettingsSnapshot _current;
    private IDisposable? _subscription;

    /// <summary>Creates the state. Nothing is read until <see cref="StartAsync"/>.</summary>
    public SettingsState(
        IOptionsMonitor<AppSettings> options,
        ISettingsStore store,
        SettingsValidator validator,
        DeviceDefaultsPostConfigure defaults,
        ILogger<SettingsState> logger)
    {
        _options = options;
        _store = store;
        _validator = validator;
        _defaults = defaults;
        _logger = logger;
        _current = new SettingsSnapshot(CreateDefaults(), []);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public SettingsSnapshot Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Refresh();
        _subscription = _options.OnChange((_, _) => Refresh());
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _subscription?.Dispose();

    /// <summary>Re-reads and re-validates the settings, then raises <see cref="Changed"/>.</summary>
    internal void Refresh()
    {
        var errors = new List<string>();

        var syntaxError = _store.GetSyntaxError();
        if (syntaxError is not null)
        {
            errors.Add(syntaxError);
        }

        AppSettings settings;
        try
        {
            settings = _options.CurrentValue;
        }
        catch (InvalidOperationException ex)
        {
            // The binder throws when a value has the wrong type (e.g. "port": "abc").
            // Only non-string values can fail to convert, so the message never contains a secret.
            errors.Add($"settings.json has a value of the wrong type: {ex.Message}");
            settings = CreateDefaults();
        }

        errors.AddRange(_validator.Validate(settings));

        Volatile.Write(ref _current, new SettingsSnapshot(settings, errors));

        if (errors.Count == 0)
        {
            LogSettingsLoaded(_logger, settings.Device.Id, settings.Mqtt.IsConfigured);
        }
        else
        {
            foreach (var error in errors)
            {
                LogInvalidSetting(_logger, error);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings CreateDefaults()
    {
        var settings = new AppSettings();
        _defaults.PostConfigure(Options.DefaultName, settings);
        return settings;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Settings loaded for device {DeviceId} (broker configured: {BrokerConfigured})")]
    private static partial void LogSettingsLoaded(ILogger logger, string deviceId, bool brokerConfigured);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid setting: {Error}")]
    private static partial void LogInvalidSetting(ILogger logger, string error);
}
