using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Hosting;

namespace SmartHomeRelay.Core.Mqtt;

/// <summary>
/// Owns the broker connection (spec 4.3): connects with a last will, publishes <c>online</c>, hands the
/// session to <see cref="IMqttSessionHandler"/>, reconnects with backoff, and reconnects when connection
/// settings change. Every clean disconnect publishes <c>offline</c> first, because a clean disconnect does
/// not trigger the last will (a prototype defect, spec 3.5).
/// </summary>
public sealed partial class MqttConnectionSupervisor : IHostedService, IRelayStatusSource, IReconnectRequester, IAsyncDisposable, IDisposable
{
    /// <summary>Budget for publishing <c>offline</c> and disconnecting (spec 6: best effort, ~2s).</summary>
    public static readonly TimeSpan OfflineTimeout = TimeSpan.FromSeconds(2);

    private readonly IMqttTransport _transport;
    private readonly IMqttSessionHandler _handler;
    private readonly ISettingsState _settings;
    private readonly ISecretProtector _secretProtector;
    private readonly ReconnectBackoff _backoff;
    private readonly TimeProvider _time;
    private readonly ILogger<MqttConnectionSupervisor> _logger;
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource _stopping = new();
    private TaskCompletionSource _disconnected = NewSignal();
    private string _activeFingerprint = string.Empty;
    private MqttTopics? _connectedTopics;
    private volatile RelayStatus _status = RelayStatus.Disconnected;
    private Task? _loop;

    /// <summary>Creates the supervisor. Nothing happens until <see cref="StartAsync"/>.</summary>
    public MqttConnectionSupervisor(
        IMqttTransport transport,
        IMqttSessionHandler handler,
        ISettingsState settings,
        ISecretProtector secretProtector,
        ReconnectBackoff backoff,
        TimeProvider time,
        ILogger<MqttConnectionSupervisor> logger)
    {
        _transport = transport;
        _handler = handler;
        _settings = settings;
        _secretProtector = secretProtector;
        _backoff = backoff;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public RelayStatus Status => _status;

    /// <summary>True if a wake-up (settings change or reconnect request) is waiting to be handled. For tests.</summary>
    internal bool HasPendingWake => _wake.Reader.Count > 0;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _settings.Changed += OnSettingsChanged;
        _transport.Disconnected += OnTransportDisconnected;
        _loop = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _settings.Changed -= OnSettingsChanged;
        _transport.Disconnected -= OnTransportDisconnected;
        await _stopping.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            // The loop publishes offline and disconnects on its way out.
            await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void RequestReconnect()
    {
        LogReconnectRequested(_logger);
        _wake.Writer.TryWrite(true);
    }

    /// <summary>
    /// Synchronous dispose (e.g. a container disposed with Dispose): stops the loop without waiting.
    /// Normal shutdown goes through <see cref="StopAsync"/>, which publishes offline first.
    /// </summary>
    public void Dispose() => _stopping.Cancel();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _stopping.Dispose();
    }

    private async Task RunAsync(CancellationToken stopping)
    {
        var attempt = 0;
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    var snapshot = _settings.Current;
                    Volatile.Write(ref _activeFingerprint, Fingerprint(snapshot));

                    var settingsStatus = RelayStatusDescriber.FromSettings(snapshot);
                    if (settingsStatus != RelayStatus.Disconnected)
                    {
                        SetStatus(settingsStatus);
                        await WaitForWakeAsync(delay: null, stopping).ConfigureAwait(false);
                        attempt = 0;
                        continue;
                    }

                    var settings = snapshot.Settings;
                    if (!TryGetPassword(settings.Mqtt, out var password))
                    {
                        SetStatus(RelayStatus.Error);
                        await WaitForWakeAsync(delay: null, stopping).ConfigureAwait(false);
                        attempt = 0;
                        continue;
                    }

                    var topics = new MqttTopics(settings.Mqtt, settings.Device);
                    var options = new MqttConnectionOptions
                    {
                        Host = settings.Mqtt.Host,
                        Port = settings.Mqtt.Port,
                        ClientId = $"shr-{settings.Device.Id}",
                        Username = string.IsNullOrEmpty(settings.Mqtt.Username) ? null : settings.Mqtt.Username,
                        Password = password,
                        WillTopic = topics.Availability,
                    };

                    SetStatus(attempt == 0 ? RelayStatus.Connecting : RelayStatus.Reconnecting);
                    var disconnected = NewSignal();
                    Volatile.Write(ref _disconnected, disconnected);

                    try
                    {
                        LogConnecting(_logger, options.ToString());
                        await _transport.ConnectAsync(options, stopping).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!stopping.IsCancellationRequested)
                    {
                        var delay = _backoff.GetDelay(attempt++);
                        if (ex is MqttConnectionRefusedException { IsAuthenticationFailure: true })
                        {
                            // Still retried (the broker's accounts may change), but the user must act: show an error.
                            LogCredentialsRejected(_logger, options.ToString(), delay.TotalSeconds);
                            SetStatus(RelayStatus.Error);
                        }
                        else
                        {
                            LogConnectFailed(_logger, ex, options.ToString(), delay.TotalSeconds);
                            SetStatus(RelayStatus.Reconnecting);
                        }

                        if (await WaitForWakeAsync(delay, stopping).ConfigureAwait(false))
                        {
                            attempt = 0;
                        }

                        continue;
                    }

                    attempt = 0;
                    Volatile.Write(ref _connectedTopics, topics);
                    await OnConnectedAsync(topics, settings, stopping).ConfigureAwait(false);

                    var dropped = await WaitForDisconnectOrWakeAsync(disconnected.Task, stopping).ConfigureAwait(false);
                    if (dropped)
                    {
                        var delay = _backoff.GetDelay(0);
                        attempt = 1;
                        LogConnectionLost(_logger, delay.TotalSeconds);
                        SetStatus(RelayStatus.Reconnecting);
                        await WaitForWakeAsync(delay, stopping).ConfigureAwait(false);
                        continue;
                    }

                    // Woken by a settings change or a reconnect request: leave cleanly, then loop.
                    await GoOfflineAsync(topics).ConfigureAwait(false);
                }
                catch (Exception ex) when (!stopping.IsCancellationRequested)
                {
                    // A bug or an unexpected library error must not end the loop: the app would
                    // never reconnect again. Log it and retry with backoff.
                    var delay = _backoff.GetDelay(attempt++);
                    LogLoopFailed(_logger, ex, delay.TotalSeconds);
                    SetStatus(RelayStatus.Reconnecting);
                    await WaitForWakeAsync(delay, stopping).ConfigureAwait(false);
                }
            }
        }
        catch (Exception) when (stopping.IsCancellationRequested)
        {
            // Stopping. MQTTnet reports a cancelled operation as MqttCommunicationTimedOutException, not
            // OperationCanceledException, so any exception here is expected.
        }
        finally
        {
            if (_transport.IsConnected && Volatile.Read(ref _connectedTopics) is { } connectedTopics)
            {
                // The topics of the live connection, not of the latest settings, which may have changed.
                await GoOfflineAsync(connectedTopics).ConfigureAwait(false);
            }

            SetStatus(RelayStatus.Disconnected);
        }
    }

    private async Task OnConnectedAsync(MqttTopics topics, AppSettings settings, CancellationToken stopping)
    {
        try
        {
            await _transport.PublishAsync(topics.Availability, MqttTopics.Online, MqttQos.AtLeastOnce, retain: true, stopping).ConfigureAwait(false);
            SetStatus(RelayStatus.Connected);
            LogConnected(_logger, topics.Availability);
            await _handler.OnConnectedAsync(topics, settings, stopping).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // While stopping this is expected and ignored. Otherwise, if the connection itself died, the
            // Disconnected signal drives the reconnect.
            if (!stopping.IsCancellationRequested)
            {
                LogSessionSetupFailed(_logger, ex);
            }
        }
    }

    /// <summary>Publishes offline, then disconnects. Required before every clean disconnect (spec 6).</summary>
    private async Task GoOfflineAsync(MqttTopics topics)
    {
        using var timeout = new CancellationTokenSource(OfflineTimeout, _time);
        try
        {
            await _transport.PublishAsync(topics.Availability, MqttTopics.Offline, MqttQos.AtLeastOnce, retain: true, timeout.Token).ConfigureAwait(false);
            await _transport.DisconnectAsync(timeout.Token).ConfigureAwait(false);
            LogDisconnected(_logger);
        }
        catch (Exception ex)
        {
            // Best effort: if this fails, the broker publishes the last will (offline) when the socket drops.
            LogGoOfflineFailed(_logger, ex);
        }
    }

    private bool TryGetPassword(MqttSettings mqtt, out string? password)
    {
        // A hand-entered plain password is used directly until it is encrypted into passwordProtected.
        if (!string.IsNullOrEmpty(mqtt.Password))
        {
            password = mqtt.Password;
            return true;
        }

        if (string.IsNullOrEmpty(mqtt.PasswordProtected))
        {
            password = null;
            return true;
        }

        try
        {
            password = _secretProtector.Unprotect(mqtt.PasswordProtected);
            return true;
        }
        catch (CryptographicException ex)
        {
            // Happens if settings.json was copied from another user or PC.
            LogPasswordDecryptFailed(_logger, ex);
            password = null;
            return false;
        }
    }

    /// <returns>True if woken by a settings change or reconnect request; false if the delay elapsed.</returns>
    private async Task<bool> WaitForWakeAsync(TimeSpan? delay, CancellationToken stopping)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var wake = _wake.Reader.ReadAsync(linked.Token).AsTask();
        if (delay is null)
        {
            await wake.ConfigureAwait(false);
            return true;
        }

        var elapsed = Task.Delay(delay.Value, _time, linked.Token);
        var first = await Task.WhenAny(wake, elapsed).ConfigureAwait(false);
        await linked.CancelAsync().ConfigureAwait(false);
        stopping.ThrowIfCancellationRequested();
        return first == wake;
    }

    /// <returns>True if the connection dropped; false if woken.</returns>
    private async Task<bool> WaitForDisconnectOrWakeAsync(Task disconnected, CancellationToken stopping)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var wake = _wake.Reader.ReadAsync(linked.Token).AsTask();
        var first = await Task.WhenAny(wake, disconnected, Task.Delay(Timeout.Infinite, linked.Token)).ConfigureAwait(false);
        await linked.CancelAsync().ConfigureAwait(false);
        stopping.ThrowIfCancellationRequested();
        return first == disconnected;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // Only reconnect when something the connection depends on changed. Feature toggles are handled
        // by the session handler without reconnecting.
        if (!string.Equals(Fingerprint(_settings.Current), Volatile.Read(ref _activeFingerprint), StringComparison.Ordinal))
        {
            LogSettingsChangedReconnect(_logger);
            _wake.Writer.TryWrite(true);
        }
    }

    private void OnTransportDisconnected(object? sender, EventArgs e) => Volatile.Read(ref _disconnected).TrySetResult();

    private void SetStatus(RelayStatus status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Fingerprint(SettingsSnapshot snapshot)
    {
        var s = snapshot.Settings;
        return string.Join(
            '\u001f',
            snapshot.IsValid,
            s.Mqtt.Host,
            s.Mqtt.Port,
            s.Mqtt.Username,
            s.Mqtt.Password,
            s.Mqtt.PasswordProtected,
            s.Mqtt.BaseTopic,
            s.Mqtt.DiscoveryPrefix,
            s.Device.Id,
            s.Device.Name);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to MQTT broker {Endpoint}")]
    private static partial void LogConnecting(ILogger logger, string endpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected; published online to {Topic}")]
    private static partial void LogConnected(ILogger logger, string topic);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not connect to {Endpoint}; retrying in {DelaySeconds:0.0}s")]
    private static partial void LogConnectFailed(ILogger logger, Exception exception, string endpoint, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "The broker {Endpoint} rejected the user name or password. Check mqtt.username and mqtt.password in settings.json. Retrying in {DelaySeconds:0.0}s")]
    private static partial void LogCredentialsRejected(ILogger logger, string endpoint, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection to the broker was lost; reconnecting in {DelaySeconds:0.0}s")]
    private static partial void LogConnectionLost(ILogger logger, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Connected, but setting up the session failed")]
    private static partial void LogSessionSetupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error in the connection loop; retrying in {DelaySeconds:0.0}s")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published offline and disconnected")]
    private static partial void LogDisconnected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish offline before disconnecting; the broker's last will covers it")]
    private static partial void LogGoOfflineFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "mqtt.passwordProtected in settings.json is not a password encrypted by this app for this Windows user. Type the password into mqtt.password instead (it is encrypted automatically) and clear passwordProtected")]
    private static partial void LogPasswordDecryptFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection settings changed; reconnecting")]
    private static partial void LogSettingsChangedReconnect(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconnect requested")]
    private static partial void LogReconnectRequested(ILogger logger);
}
