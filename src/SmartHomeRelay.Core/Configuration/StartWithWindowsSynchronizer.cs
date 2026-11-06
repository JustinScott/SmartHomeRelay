using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// Keeps the "start with Windows" entry in line with <see cref="AppSettings.StartWithWindows"/> (spec 7.1): at start
/// and whenever valid settings change. The setting is the source of truth; the installer doesn't create the entry.
/// </summary>
public sealed partial class StartWithWindowsSynchronizer(ISettingsState settings, IStartupRegistration registration, ILogger<StartWithWindowsSynchronizer> logger)
    : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!registration.IsInstalled)
        {
            LogNotInstalled(logger);
            return Task.CompletedTask;
        }

        Apply();
        settings.Changed += OnChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        settings.Changed -= OnChanged;
        return Task.CompletedTask;
    }

    private void OnChanged(object? sender, EventArgs e) => Apply();

    private void Apply()
    {
        var snapshot = settings.Current;
        if (!snapshot.IsValid)
        {
            return; // A broken file runs on defaults; don't flip the entry because of that.
        }

        try
        {
            if (registration.Apply(snapshot.Settings.StartWithWindows))
            {
                LogApplied(logger, snapshot.Settings.StartWithWindows);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Not running from the installed folder; the start-with-Windows entry is left alone")]
    private static partial void LogNotInstalled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Start with Windows set to {Enabled}")]
    private static partial void LogApplied(ILogger logger, bool enabled);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not update the start-with-Windows entry")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
