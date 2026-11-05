using System.Text.Json;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Entities;

/// <summary>What a <see cref="CustomComponent"/> needs to build its discovery config.</summary>
public sealed class CustomComponentTopics
{
    private readonly MqttTopics _topics;

    /// <summary>Creates the topics for <paramref name="componentName"/>.</summary>
    public CustomComponentTopics(MqttTopics topics, string componentName, DeviceSettings device, string swVersion)
    {
        ArgumentNullException.ThrowIfNull(topics);
        _topics = topics;
        ComponentName = componentName;
        Device = device;
        SwVersion = swVersion;
    }

    /// <summary>The component's name.</summary>
    public string ComponentName { get; }

    /// <summary>The device the config belongs to.</summary>
    public DeviceSettings Device { get; }

    /// <summary>The app version for the device block.</summary>
    public string SwVersion { get; }

    /// <summary>The device's availability topic (<c>online</c>/<c>offline</c>).</summary>
    public string Availability => _topics.Availability;

    /// <summary><c>{baseTopic}/{deviceId}/{component}/{state}</c>.</summary>
    public string State(string state) => _topics.CustomState(ComponentName, state);

    /// <summary><c>{baseTopic}/{deviceId}/{component}/command/{command}</c>.</summary>
    public string Command(string command) => _topics.CustomCommand(ComponentName, command);

    /// <summary>Writes the same <c>device</c> block every entity has, so the component lands on the same HA device.</summary>
    public void WriteDevice(Utf8JsonWriter json) => DiscoveryPayloadBuilder.WriteDevice(json, Device, SwVersion);

    /// <summary>
    /// Writes a <c>device</c> block for a device of its own, <c>{device id}_{component}</c>, named
    /// <c>{device name} {displayName}</c>. For integrations other than HA's MQTT one, which create their own device:
    /// sharing our device's identifier there would give two devices the same identifier.
    /// </summary>
    public void WriteOwnDevice(Utf8JsonWriter json, string displayName) =>
        DiscoveryPayloadBuilder.WriteDevice(json, OwnDeviceIdentifier, $"{Device.Name} {displayName}", SwVersion);

    /// <summary>The identifier <see cref="WriteOwnDevice"/> uses: <c>{device id}_{component}</c>.</summary>
    public string OwnDeviceIdentifier => $"{Device.Id}_{ComponentName}";
}
