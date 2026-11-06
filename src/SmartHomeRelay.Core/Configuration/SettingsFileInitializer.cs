using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// First run (spec 7): creates <c>settings.json</c> with defaults, opens the settings window (milestone 10;
/// before that it opened the file), and tells the user what to fill in. The device ID written is the current host name, which pins it so a later host
/// name change doesn't create new HA entities. An existing file is never overwritten.
/// </summary>
public sealed partial class SettingsFileInitializer(
    ISettingsStore store,
    ISettingsState state,
    IAppPaths paths,
    ISettingsWindow window,
    IUserNotifier notifier,
    ILogger<SettingsFileInitializer> logger) : IHostedService
{
    internal const string WelcomeTitle = "Welcome to Smart Home Relay";

    internal const string WelcomeMessage =
        "Enter your MQTT broker's address, user name, and password, then save. " +
        "The app connects on its own.";

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (store.Exists())
        {
            return;
        }

        try
        {
            await store.SaveAsync(state.Current.Settings, cancellationToken).ConfigureAwait(false);
            LogCreated(logger, paths.SettingsFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the app runs on in-memory defaults and reports the problem.
            LogCreateFailed(logger, ex, paths.SettingsFilePath);
            return;
        }

        notifier.Notify(WelcomeTitle, WelcomeMessage);
        try
        {
            window.Open();
        }
        catch (Exception ex)
        {
            // The balloon still tells the user what to do; the tray's Settings item opens the window too.
            LogOpenFailed(logger, ex);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Created default settings file at {Path}")]
    private static partial void LogCreated(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not create the settings file at {Path}")]
    private static partial void LogCreateFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open the settings window")]
    private static partial void LogOpenFailed(ILogger logger, Exception exception);
}
