using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// First run (spec 7): creates <c>settings.json</c> with defaults, opens it for editing, and tells the
/// user what to fill in. The device ID written is the current host name, which pins it so a later host
/// name change doesn't create new HA entities. An existing file is never overwritten.
/// </summary>
public sealed partial class SettingsFileInitializer(
    ISettingsStore store,
    ISettingsState state,
    IAppPaths paths,
    IShellLauncher shell,
    IUserNotifier notifier,
    ILogger<SettingsFileInitializer> logger) : IHostedService
{
    internal const string WelcomeTitle = "Welcome to Smart Home Relay";

    internal const string WelcomeMessage =
        "Fill in host, username, and password under \"mqtt\" in settings.json, then save. " +
        "The password is encrypted automatically and the app connects on its own.";

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
            shell.OpenFile(paths.SettingsFilePath);
        }
        catch (Exception ex)
        {
            // The balloon still tells the user where to look; the tray's Settings item opens it too.
            LogOpenFailed(logger, ex, paths.SettingsFilePath);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Created default settings file at {Path}")]
    private static partial void LogCreated(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not create the settings file at {Path}")]
    private static partial void LogCreateFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open {Path} for editing")]
    private static partial void LogOpenFailed(ILogger logger, Exception exception, string path);
}
