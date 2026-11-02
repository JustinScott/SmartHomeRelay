using System.Text.Json;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Mqtt;

public sealed class DiscoveryPayloadBuilderTests
{
    private const string Version = "1.2.3";
    private static readonly EntityCommandHandler s_noop = (_, _) => Task.CompletedTask;

    [Fact]
    public void Build_AnyEntity_HasValidDeviceBlock()
    {
        // Regression (spec 3.5 #2): the prototype built an invalid device block.
        using var json = Parse(Build(new EntityDescriptor("uptime", "Uptime", EntityComponent.Sensor)));
        var device = json.RootElement.GetProperty("device");

        device.GetProperty("identifiers").EnumerateArray().Select(e => e.GetString()).ShouldBe(["DESKTOP-ABC123"]);
        device.GetProperty("name").GetString().ShouldBe("Desk PC");
        device.GetProperty("manufacturer").GetString().ShouldBe("Smart Home Relay");
        device.GetProperty("model").GetString().ShouldBe("Windows PC");
        device.GetProperty("sw_version").GetString().ShouldBe(Version);
    }

    [Fact]
    public void Build_AnyEntity_AvailabilityPayloadsMatchStatusTopic()
    {
        // Regression (spec 3.5 #2): the prototype used ON/OFF while the status topic carries online/offline.
        using var json = Parse(Build(new EntityDescriptor("uptime", "Uptime", EntityComponent.Sensor)));
        var root = json.RootElement;

        root.GetProperty("availability_topic").GetString().ShouldBe("shr/DESKTOP-ABC123/status");
        root.GetProperty("payload_available").GetString().ShouldBe(MqttTopics.Online);
        root.GetProperty("payload_not_available").GetString().ShouldBe(MqttTopics.Offline);
    }

    [Fact]
    public void Build_AnyEntity_UsesSanitizedUniqueId()
    {
        using var json = Parse(Build(new EntityDescriptor("cpu_usage", "CPU usage", EntityComponent.Sensor)));

        json.RootElement.GetProperty("unique_id").GetString().ShouldBe("desktop_abc123_cpu_usage");
    }

    [Fact]
    public void Build_Button_HasCommandTopicButNoStateTopic()
    {
        using var json = Parse(Build(new EntityDescriptor("sleep", "Sleep", EntityComponent.Button) { CommandHandler = s_noop }));
        var root = json.RootElement;

        root.GetProperty("command_topic").GetString().ShouldBe("shr/DESKTOP-ABC123/sleep/set");
        root.TryGetProperty("state_topic", out _).ShouldBeFalse();
        root.GetProperty("qos").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void Build_Sensor_HasStateTopicButNoCommandTopic()
    {
        using var json = Parse(Build(new EntityDescriptor("uptime", "Uptime", EntityComponent.Sensor)));
        var root = json.RootElement;

        root.GetProperty("state_topic").GetString().ShouldBe("shr/DESKTOP-ABC123/uptime/state");
        root.TryGetProperty("command_topic", out _).ShouldBeFalse();
    }

    [Fact]
    public void Snapshot_Sensor() => Snapshot.ShouldMatchApproved(
        Build(new EntityDescriptor("cpu_usage", "CPU usage", EntityComponent.Sensor)
        {
            UnitOfMeasurement = "%",
            StateClass = "measurement",
            Icon = "mdi:cpu-64-bit",
        }),
        "sensor");

    [Fact]
    public void Snapshot_EnumSensorDiagnostic() => Snapshot.ShouldMatchApproved(
        Build(new EntityDescriptor("presence_state", "Presence", EntityComponent.Sensor)
        {
            DeviceClass = "enum",
            Options = ["active", "idle", "locked"],
            Category = EntityCategory.Diagnostic,
        }),
        "sensor_enum");

    [Fact]
    public void Snapshot_BinarySensor() => Snapshot.ShouldMatchApproved(
        Build(new EntityDescriptor("session_locked", "Session locked", EntityComponent.BinarySensor) { DeviceClass = "lock" }),
        "binary_sensor");

    [Fact]
    public void Snapshot_Button() => Snapshot.ShouldMatchApproved(
        Build(new EntityDescriptor("sleep", "Sleep", EntityComponent.Button) { CommandHandler = s_noop, Icon = "mdi:power-sleep" }),
        "button");

    [Fact]
    public void Snapshot_Switch() => Snapshot.ShouldMatchApproved(
        Build(new EntityDescriptor("muted", "Muted", EntityComponent.Switch) { CommandHandler = s_noop, Category = EntityCategory.Config }),
        "switch");

    [Fact]
    public void Snapshot_Number() => Snapshot.ShouldMatchApproved(
        Build(new EntityDescriptor("volume", "Volume", EntityComponent.Number)
        {
            CommandHandler = s_noop,
            Range = new NumberRange(0, 100, 1),
            UnitOfMeasurement = "%",
        }),
        "number");

    private static string Build(EntityDescriptor entity)
    {
        var settings = FakeSettingsState.Configured();
        return DiscoveryPayloadBuilder.Build(entity, new MqttTopics(settings.Mqtt, settings.Device), settings.Device, Version);
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);
}
