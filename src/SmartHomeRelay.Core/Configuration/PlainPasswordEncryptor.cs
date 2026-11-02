using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// Encrypts a password the user typed into <c>settings.json</c> (<c>"mqtt": { "password": "..." }</c>)
/// into <c>passwordProtected</c> and rewrites the file without the plain text (spec 7). Runs at startup
/// and whenever the file changes.
/// </summary>
public sealed partial class PlainPasswordEncryptor(
    ISettingsState settings,
    ISettingsStore store,
    ISecretProtector protector,
    ILogger<PlainPasswordEncryptor> logger) : IHostedService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        settings.Changed += OnSettingsChanged;
        await EncryptIfNeededAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        settings.Changed -= OnSettingsChanged;
        await _stopping.CancelAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
        _stopping.Dispose();
    }

    /// <summary>Encrypts and saves if the current settings contain a plain password.</summary>
    internal async Task EncryptIfNeededAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = settings.Current.Settings;
            if (string.IsNullOrEmpty(current.Mqtt.Password) || store.GetSyntaxError() is not null)
            {
                return;
            }

            // Copy via JSON: Password is [JsonIgnore], so the copy (and the saved file) never contains it.
            var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(current, SettingsJson.Options), SettingsJson.Options)!;
            copy.Mqtt.PasswordProtected = protector.Protect(current.Mqtt.Password);

            await store.SaveAsync(copy, cancellationToken).ConfigureAwait(false);
            LogEncrypted(logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEncryptFailed(logger, ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var token = _stopping.Token;
        _ = Task.Run(() => EncryptIfNeededAsync(token), CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Encrypted the MQTT password entered in settings.json and removed the plain text")]
    private static partial void LogEncrypted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not encrypt the MQTT password in settings.json; the plain text is still in the file")]
    private static partial void LogEncryptFailed(ILogger logger, Exception exception);
}
