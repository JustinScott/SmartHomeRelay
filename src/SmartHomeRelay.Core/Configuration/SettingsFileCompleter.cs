using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// After an upgrade (spec 7): adds settings that are new in this version (e.g. <c>features.hardware</c>) to
/// an existing <c>settings.json</c>, with their defaults, so users can see and edit them. Only adds, never
/// changes a value, and only when the file is valid; a file with comments is left alone and the missing
/// settings are listed in the log instead.
/// </summary>
public sealed partial class SettingsFileCompleter(
    ISettingsStore store,
    ISettingsState state,
    ILogger<SettingsFileCompleter> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // A broken or invalid file is the user's to fix; never rewrite it (spec 7).
        if (!store.Exists() || store.GetSyntaxError() is not null || !state.Current.IsValid)
        {
            return;
        }

        try
        {
            var result = await store.AddMissingAsync(state.Current.Settings, cancellationToken).ConfigureAwait(false);
            if (result.Written)
            {
                LogAdded(logger, string.Join(", ", result.Added));
            }
            else if (result.HasComments)
            {
                LogSkippedComments(logger, string.Join(", ", result.Added));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // Not fatal: the app uses defaults for anything missing.
            LogFailed(logger, ex);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Added new settings to settings.json with their defaults: {Paths}")]
    private static partial void LogAdded(ILogger logger, string paths);

    [LoggerMessage(Level = LogLevel.Information, Message = "settings.json has comments, so it was not rewritten. New settings using their defaults: {Paths}")]
    private static partial void LogSkippedComments(ILogger logger, string paths);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not add new settings to settings.json; their defaults are used")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
