using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// Creates <c>settings.json</c> with defaults on first run (spec 7). The device ID written is the
/// current host name, which pins it so a later host name change doesn't create new HA entities.
/// An existing file is never overwritten.
/// </summary>
public sealed partial class SettingsFileInitializer(
    ISettingsStore store,
    ISettingsState state,
    IAppPaths paths,
    ILogger<SettingsFileInitializer> logger) : IHostedService
{
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
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Created default settings file at {Path}")]
    private static partial void LogCreated(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not create the settings file at {Path}")]
    private static partial void LogCreateFailed(ILogger logger, Exception exception, string path);
}
