using System.Text;
using System.Text.Json;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;

namespace SmartHomeRelay.Core.Mqtt;

/// <summary>
/// Builds HA MQTT discovery configs (spec 4.1, 4.3). Fixes the prototype's defects: every config has a
/// valid <c>device</c> block, and availability payloads match what is published to the status topic.
/// Output is deterministic so it can be snapshot-tested.
/// </summary>
public static class DiscoveryPayloadBuilder
{
    /// <summary>The HA device manufacturer.</summary>
    public const string Manufacturer = "Smart Home Relay";

    /// <summary>The HA device model.</summary>
    public const string Model = "Windows PC";

    /// <summary>Command payload HA sends when a button is pressed.</summary>
    public const string ButtonPressPayload = "PRESS";

    /// <summary>ON payload for switches and binary sensors.</summary>
    public const string On = "ON";

    /// <summary>OFF payload for switches and binary sensors.</summary>
    public const string Off = "OFF";

    private static readonly JsonWriterOptions s_writerOptions = new() { Indented = false };

    /// <summary>Builds the retained config payload for <paramref name="entity"/>.</summary>
    public static string Build(EntityDescriptor entity, MqttTopics topics, DeviceSettings device, string swVersion)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(device);

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, s_writerOptions))
        {
            json.WriteStartObject();
            json.WriteString("name", entity.Name);
            json.WriteString("unique_id", UniqueIds.For(device.Id, entity.Key));
            json.WriteBoolean("has_entity_name", true);

            if (entity.ReportsConnection)
            {
                // No availability: the entity reads the availability topic itself and shows off while offline.
            }
            else if (entity.AvailableWhen is { } availability)
            {
                WriteAvailabilityList(json, topics, availability);
            }
            else
            {
                json.WriteString("availability_topic", topics.Availability);
                json.WriteString("payload_available", MqttTopics.Online);
                json.WriteString("payload_not_available", MqttTopics.Offline);
            }

            if (entity.HasState)
            {
                json.WriteString("state_topic", entity.ReportsConnection ? topics.Availability : topics.State(entity.Key));
            }

            if (entity.AcceptsCommands)
            {
                json.WriteString("command_topic", topics.CommandFor(entity));
                json.WriteNumber("qos", 1);
            }

            WriteComponentFields(json, entity);
            WriteOptional(json, "device_class", entity.DeviceClass);
            WriteOptional(json, "unit_of_measurement", entity.UnitOfMeasurement);
            WriteOptional(json, "state_class", entity.StateClass);
            WriteOptional(json, "icon", entity.Icon);

            if (entity.Category is { } category)
            {
                json.WriteString("entity_category", category == EntityCategory.Config ? "config" : "diagnostic");
            }

            if (entity.Options is { Count: > 0 } options)
            {
                json.WriteStartArray("options");
                foreach (var option in options)
                {
                    json.WriteStringValue(option);
                }

                json.WriteEndArray();
            }

            WriteDevice(json, device, swVersion);

            json.WriteStartObject("origin");
            json.WriteString("name", Manufacturer);
            json.WriteString("sw_version", swVersion);
            json.WriteEndObject();

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes the <c>device</c> block shared by every entity.</summary>
    public static void WriteDevice(Utf8JsonWriter json, DeviceSettings device, string swVersion)
    {
        ArgumentNullException.ThrowIfNull(device);
        WriteDevice(json, device.Id, device.Name, swVersion);
    }

    /// <summary>
    /// Writes a <c>device</c> block with our manufacturer and model and the given identifier and name, plus
    /// <c>via_device</c> (the identifier of the device it is connected through) when given.
    /// </summary>
    public static void WriteDevice(Utf8JsonWriter json, string identifier, string name, string swVersion, string? viaDevice = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        json.WriteStartObject("device");
        json.WriteStartArray("identifiers");
        json.WriteStringValue(identifier);
        json.WriteEndArray();
        json.WriteString("name", name);
        json.WriteString("manufacturer", Manufacturer);
        json.WriteString("model", Model);
        json.WriteString("sw_version", swVersion);
        if (!string.IsNullOrEmpty(viaDevice))
        {
            json.WriteString("via_device", viaDevice);
        }

        json.WriteEndObject();
    }

    private static void WriteComponentFields(Utf8JsonWriter json, EntityDescriptor entity)
    {
        switch (entity.Component)
        {
            case EntityComponent.Button:
                json.WriteString("payload_press", ButtonPressPayload);
                break;
            case EntityComponent.Switch:
                json.WriteString("payload_on", On);
                json.WriteString("payload_off", Off);
                json.WriteString("state_on", On);
                json.WriteString("state_off", Off);
                break;
            case EntityComponent.BinarySensor:
                json.WriteString("payload_on", entity.ReportsConnection ? MqttTopics.Online : On);
                json.WriteString("payload_off", entity.ReportsConnection ? MqttTopics.Offline : Off);
                break;
            case EntityComponent.Number when entity.Range is { } range:
                json.WriteNumber("min", range.Min);
                json.WriteNumber("max", range.Max);
                json.WriteNumber("step", range.Step);
                if (entity.Mode is { } mode)
                {
                    json.WriteString("mode", mode == NumberMode.Slider ? "slider" : "box");
                }

                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Device availability plus the status entity's state, both required (<c>availability_mode: all</c>).
    /// The template maps the status to online/offline; its states are validated as plain words.
    /// </summary>
    private static void WriteAvailabilityList(Utf8JsonWriter json, MqttTopics topics, EntityAvailability availability)
    {
        json.WriteStartArray("availability");

        json.WriteStartObject();
        json.WriteString("topic", topics.Availability);
        json.WriteString("payload_available", MqttTopics.Online);
        json.WriteString("payload_not_available", MqttTopics.Offline);
        json.WriteEndObject();

        var states = string.Join(", ", availability.AvailableStates.Select(state => $"'{state}'"));
        json.WriteStartObject();
        json.WriteString("topic", topics.State(availability.StatusKey));
        json.WriteString("value_template", $"{{{{ '{MqttTopics.Online}' if value in [{states}] else '{MqttTopics.Offline}' }}}}");
        json.WriteString("payload_available", MqttTopics.Online);
        json.WriteString("payload_not_available", MqttTopics.Offline);
        json.WriteEndObject();

        json.WriteEndArray();
        json.WriteString("availability_mode", "all");
    }

    private static void WriteOptional(Utf8JsonWriter json, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            json.WriteString(name, value);
        }
    }
}
