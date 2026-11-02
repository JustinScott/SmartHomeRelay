using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;

namespace SmartHomeRelay.Core.Mqtt;

/// <summary>
/// The topic layout (spec 4.2). <c>shr/{deviceId}/status</c> and <c>shr/{deviceId}/commands/notification</c>
/// are a compatibility contract with existing HA automations and must not change.
/// </summary>
public sealed class MqttTopics
{
    /// <summary>Payload published to <see cref="Availability"/> when connected.</summary>
    public const string Online = "online";

    /// <summary>Payload published to <see cref="Availability"/> (and the last will) when not connected.</summary>
    public const string Offline = "offline";

    private readonly string _deviceRoot;
    private readonly string _discoveryPrefix;

    /// <summary>Builds the topics for the given settings.</summary>
    public MqttTopics(MqttSettings mqtt, DeviceSettings device)
    {
        ArgumentNullException.ThrowIfNull(mqtt);
        ArgumentNullException.ThrowIfNull(device);

        DeviceId = device.Id;
        _discoveryPrefix = mqtt.DiscoveryPrefix;
        _deviceRoot = $"{mqtt.BaseTopic}/{device.Id}";
        Availability = $"{_deviceRoot}/status";
        HomeAssistantStatus = $"{mqtt.DiscoveryPrefix}/status";
        NotificationCommand = $"{_deviceRoot}/commands/notification";
    }

    /// <summary>The device ID exactly as configured (used in topics; not sanitized).</summary>
    public string DeviceId { get; }

    /// <summary><c>{baseTopic}/{deviceId}/status</c>: <c>online</c>/<c>offline</c>, retained.</summary>
    public string Availability { get; }

    /// <summary><c>{discoveryPrefix}/status</c>: HA publishes <c>online</c> here after it restarts.</summary>
    public string HomeAssistantStatus { get; }

    /// <summary><c>{baseTopic}/{deviceId}/commands/notification</c> (prototype-compatible, spec 5.6).</summary>
    public string NotificationCommand { get; }

    /// <summary><c>{baseTopic}/{deviceId}/{entityKey}/state</c>.</summary>
    public string State(string entityKey) => $"{_deviceRoot}/{entityKey}/state";

    /// <summary><c>{baseTopic}/{deviceId}/{entityKey}/set</c>.</summary>
    public string Command(string entityKey) => $"{_deviceRoot}/{entityKey}/set";

    /// <summary><c>{discoveryPrefix}/{component}/{deviceId}/{entityKey}/config</c>, retained.</summary>
    public string Discovery(EntityComponent component, string entityKey) =>
        $"{_discoveryPrefix}/{ComponentName(component)}/{DeviceId}/{entityKey}/config";

    /// <summary>Returns the entity key if <paramref name="topic"/> is one of this device's command topics.</summary>
    public bool TryGetCommandKey(string topic, out string entityKey)
    {
        ArgumentNullException.ThrowIfNull(topic);

        entityKey = string.Empty;
        var prefix = _deviceRoot + "/";
        const string suffix = "/set";
        if (!topic.StartsWith(prefix, StringComparison.Ordinal) || !topic.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var key = topic[prefix.Length..^suffix.Length];
        if (!EntityValidator.IsValidKey(key))
        {
            return false;
        }

        entityKey = key;
        return true;
    }

    /// <summary>The HA component name used in discovery topics, e.g. <c>binary_sensor</c>.</summary>
    public static string ComponentName(EntityComponent component) => component switch
    {
        EntityComponent.Sensor => "sensor",
        EntityComponent.BinarySensor => "binary_sensor",
        EntityComponent.Button => "button",
        EntityComponent.Switch => "switch",
        EntityComponent.Number => "number",
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, "Unknown component."),
    };
}
