using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;

namespace SmartHomeRelay.Core.Mqtt;

/// <summary>
/// Remove from Home Assistant (spec 4.4): clears every retained message this PC left on the broker, so HA deletes
/// its device and entities. Run by the uninstaller and the tray menu, never while the app's own connection is up
/// (it would announce everything again).
/// <para>
/// It finds the messages by subscribing rather than from the entity list, so entities of older versions, other
/// hardware, and turned-off features go too. A discovery config is only cleared when its <c>device</c> block is
/// this PC's (identifier <c>{id}</c> or <c>{id}_…</c>), so another program that uses the same device name in its
/// topics is left alone. Everything under <c>{baseTopic}/{id}/</c> is this app's and is cleared, availability last.
/// </para>
/// </summary>
public sealed partial class HomeAssistantRemover
{
    /// <summary>How long the broker gets to accept the connection.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<IMqttTransport> _createTransport;
    private readonly ISecretProtector _protector;
    private readonly ILogger<HomeAssistantRemover> _logger;
    private readonly TimeSpan _quietPeriod;
    private readonly TimeSpan _maxCollectTime;

    /// <summary>Creates the remover.</summary>
    /// <param name="createTransport">Creates the remover's own short-lived connection.</param>
    /// <param name="protector">Decrypts the saved MQTT password.</param>
    /// <param name="logger">The log.</param>
    /// <param name="maxCollectTime">The longest time spent collecting retained messages.</param>
    /// <param name="quietPeriod">
    /// Retained messages arrive right after subscribing, with no end marker: collecting stops once none arrived
    /// for this long (default 1 second), and after <paramref name="maxCollectTime"/> at most (default 5 seconds).
    /// </param>
    public HomeAssistantRemover(
        Func<IMqttTransport> createTransport,
        ISecretProtector protector,
        ILogger<HomeAssistantRemover> logger,
        TimeSpan? quietPeriod = null,
        TimeSpan? maxCollectTime = null)
    {
        _createTransport = createTransport;
        _protector = protector;
        _logger = logger;
        _quietPeriod = quietPeriod ?? TimeSpan.FromSeconds(1);
        _maxCollectTime = maxCollectTime ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Removes the PC described by <paramref name="settings"/>. Never throws for broker problems.</summary>
    public async Task<RemovalResult> RemoveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var mqtt = settings.Mqtt;
        var deviceId = settings.Device.Id.Trim();
        var host = mqtt.Host.Trim();
        if (host.Length == 0 || deviceId.Length == 0)
        {
            return new(RemovalOutcome.NotConfigured, 0, "Smart Home Relay isn't set up, so there's nothing to remove.");
        }

        // These become parts of wildcard subscriptions, so they must be plain topic levels.
        if (!SettingsValidator.IsValidDeviceId(deviceId) || !IsPlainTopic(mqtt.BaseTopic) || !IsPlainTopic(mqtt.DiscoveryPrefix))
        {
            return new(RemovalOutcome.Failed, 0, "The device ID or MQTT topics in settings.json aren't valid, so nothing was removed.");
        }

        string? password = null;
        if (mqtt.PasswordProtected.Length > 0)
        {
            try
            {
                password = _protector.Unprotect(mqtt.PasswordProtected);
            }
            catch (CryptographicException)
            {
                return new(RemovalOutcome.Failed, 0, "The saved MQTT password can't be read on this PC, so nothing was removed.");
            }
        }

        var root = $"{mqtt.BaseTopic}/{deviceId}";
        var discoveryFilter = $"{mqtt.DiscoveryPrefix}/+/{deviceId}/#";
        var options = new MqttConnectionOptions
        {
            Host = host,
            Port = mqtt.Port,
            ClientId = $"shr-{deviceId}-remove",
            Username = mqtt.Username.Length > 0 ? mqtt.Username : null,
            Password = password,
            WillTopic = $"{root}/removal",
            WillRetain = false,
            ConnectTimeout = ConnectTimeout,
        };

        var transport = _createTransport();
        await using (transport.ConfigureAwait(false))
        {
            var retained = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            var lastArrival = Stopwatch.GetTimestamp();
            void OnMessage(object? sender, MqttMessage message)
            {
                // Only stored messages matter; an empty one is never stored.
                if (message.Retained && message.Payload.Length > 0)
                {
                    retained[message.Topic] = message.Payload;
                    Interlocked.Exchange(ref lastArrival, Stopwatch.GetTimestamp());
                }
            }

            transport.MessageReceived += OnMessage;
            try
            {
                await transport.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
                Interlocked.Exchange(ref lastArrival, Stopwatch.GetTimestamp());
                var started = Stopwatch.GetTimestamp();
                await transport.SubscribeAsync([discoveryFilter, $"{root}/#"], cancellationToken).ConfigureAwait(false);

                while (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastArrival)) < _quietPeriod
                    && Stopwatch.GetElapsedTime(started) < _maxCollectTime)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                }

                transport.MessageReceived -= OnMessage;
                var snapshot = retained.ToArray();

                // Discovery first, so HA deletes the entities before their states and availability disappear.
                var configs = snapshot.Where(m => !m.Key.StartsWith(root + "/", StringComparison.Ordinal)).OrderBy(m => m.Key, StringComparer.Ordinal).ToList();
                var removed = 0;
                var mediaPlayer = false;
                foreach (var (topic, payload) in configs)
                {
                    if (!BelongsTo(payload, deviceId))
                    {
                        LogForeignConfigKept(_logger, topic);
                        continue;
                    }

                    await transport.PublishAsync(topic, string.Empty, MqttQos.AtLeastOnce, retain: true, cancellationToken).ConfigureAwait(false);
                    removed++;
                    mediaPlayer |= topic.StartsWith($"{mqtt.DiscoveryPrefix}/media_player/", StringComparison.Ordinal);
                }

                var availability = $"{root}/status";
                var own = snapshot.Where(m => m.Key.StartsWith(root + "/", StringComparison.Ordinal))
                    .Select(m => m.Key)
                    .OrderBy(t => t == availability)
                    .ThenBy(t => t, StringComparer.Ordinal)
                    .ToList();
                foreach (var topic in own)
                {
                    await transport.PublishAsync(topic, string.Empty, MqttQos.AtLeastOnce, retain: true, cancellationToken).ConfigureAwait(false);
                }

                await transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                LogRemoved(_logger, deviceId, removed, own.Count);

                var message = removed == 0
                    ? $"{deviceId} wasn't in Home Assistant, so there was nothing to remove."
                    : $"Removed {deviceId} from Home Assistant ({removed} {(removed == 1 ? "entity" : "entities")}).";
                if (mediaPlayer)
                {
                    message += " The MQTT Media Player add-on keeps its player: delete it under Settings → Devices & services → MQTT Media Player.";
                }

                return new(RemovalOutcome.Removed, removed, message);
            }
            catch (MqttConnectionRefusedException ex)
            {
                LogFailed(_logger, ex, host);
                return new(RemovalOutcome.Failed, 0, $"{host}:{mqtt.Port} refused the connection ({ex.Message}), so nothing was removed.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogFailed(_logger, ex, host);
                return new(RemovalOutcome.Failed, 0, $"Could not reach the MQTT broker at {host}:{mqtt.Port}, so nothing was removed.");
            }
            finally
            {
                transport.MessageReceived -= OnMessage;
            }
        }
    }

    /// <summary>True when the config's <c>device</c> identifiers include <paramref name="deviceId"/> or <c>{deviceId}_…</c>.</summary>
    internal static bool BelongsTo(string payload, string deviceId)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !(document.RootElement.TryGetProperty("device", out var device) || document.RootElement.TryGetProperty("dev", out device))
                || device.ValueKind != JsonValueKind.Object
                || !(device.TryGetProperty("identifiers", out var ids) || device.TryGetProperty("ids", out ids)))
            {
                return false;
            }

            IEnumerable<JsonElement> values = ids.ValueKind == JsonValueKind.Array ? ids.EnumerateArray() : [ids];
            return values.Any(v => v.ValueKind == JsonValueKind.String
                && v.GetString() is { } id
                && (id == deviceId || id.StartsWith(deviceId + "_", StringComparison.Ordinal)));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsPlainTopic(string topic) =>
        topic.Length > 0 && !topic.Contains('+', StringComparison.Ordinal) && !topic.Contains('#', StringComparison.Ordinal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {DeviceId} from Home Assistant: cleared {Configs} discovery configs and {Own} retained topics")]
    private static partial void LogRemoved(ILogger logger, string deviceId, int configs, int own);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kept {Topic}: its device is not this PC's")]
    private static partial void LogForeignConfigKept(ILogger logger, string topic);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove this PC from Home Assistant: the broker {Host} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string host);
}

/// <summary>How <see cref="HomeAssistantRemover.RemoveAsync"/> ended.</summary>
public enum RemovalOutcome
{
    /// <summary>Everything found was cleared (possibly nothing).</summary>
    Removed,

    /// <summary>No broker or device ID is set up.</summary>
    NotConfigured,

    /// <summary>Nothing was removed: the broker couldn't be reached or refused, or the settings can't be used.</summary>
    Failed,
}

/// <summary>The outcome of Remove from Home Assistant.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="EntitiesRemoved">How many discovery configs (entities) were cleared.</param>
/// <param name="Message">A sentence for the user. Never contains the password.</param>
public sealed record RemovalResult(RemovalOutcome Outcome, int EntitiesRemoved, string Message);
