using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using SmartHomeRelay.Core.Configuration;

namespace SmartHomeRelay.App.Logging;

/// <summary>Applies <see cref="AppSettings.LogLevel"/> to the Serilog level switch, live (spec 11).</summary>
internal sealed class LogLevelSynchronizer(ISettingsState settings, LoggingLevelSwitch levelSwitch) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Apply();
        settings.Changed += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        settings.Changed -= OnSettingsChanged;
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Apply();

    private void Apply() => levelSwitch.MinimumLevel = settings.Current.Settings.LogLevel switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };
}
