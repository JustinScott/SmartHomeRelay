using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Mqtt;

public sealed class MqttTopicsTests
{
    private readonly MqttTopics _topics = Create();

    [Fact]
    public void Availability_MatchesPrototypeContract()
    {
        // Existing HA automations depend on this exact topic (AGENTS.md hard constraint).
        _topics.Availability.ShouldBe("shr/DESKTOP-ABC123/status");
    }

    [Fact]
    public void NotificationCommand_MatchesPrototypeContract()
    {
        _topics.NotificationCommand.ShouldBe("shr/DESKTOP-ABC123/commands/notification");
    }

    [Fact]
    public void StateAndCommand_FollowSpecLayout()
    {
        _topics.State("cpu_usage").ShouldBe("shr/DESKTOP-ABC123/cpu_usage/state");
        _topics.Command("sleep").ShouldBe("shr/DESKTOP-ABC123/sleep/set");
    }

    [Theory]
    [InlineData(EntityComponent.Sensor, "homeassistant/sensor/DESKTOP-ABC123/cpu_usage/config")]
    [InlineData(EntityComponent.BinarySensor, "homeassistant/binary_sensor/DESKTOP-ABC123/cpu_usage/config")]
    [InlineData(EntityComponent.Button, "homeassistant/button/DESKTOP-ABC123/cpu_usage/config")]
    [InlineData(EntityComponent.Switch, "homeassistant/switch/DESKTOP-ABC123/cpu_usage/config")]
    [InlineData(EntityComponent.Number, "homeassistant/number/DESKTOP-ABC123/cpu_usage/config")]
    [InlineData(EntityComponent.Select, "homeassistant/select/DESKTOP-ABC123/cpu_usage/config")]
    public void Discovery_UsesComponentAndDeviceId(EntityComponent component, string expected)
    {
        _topics.Discovery(component, "cpu_usage").ShouldBe(expected);
    }

    [Fact]
    public void HomeAssistantStatus_UsesDiscoveryPrefix()
    {
        _topics.HomeAssistantStatus.ShouldBe("homeassistant/status");
    }

    [Fact]
    public void Topics_UseConfiguredPrefixes()
    {
        var settings = FakeSettingsState.Configured();
        settings.Mqtt.BaseTopic = "home/pcs";
        settings.Mqtt.DiscoveryPrefix = "ha";
        var topics = new MqttTopics(settings.Mqtt, settings.Device);

        topics.Availability.ShouldBe("home/pcs/DESKTOP-ABC123/status");
        topics.Discovery(EntityComponent.Sensor, "uptime").ShouldBe("ha/sensor/DESKTOP-ABC123/uptime/config");
    }

    [Fact]
    public void TryGetCommandKey_OwnCommandTopic_ReturnsKey()
    {
        _topics.TryGetCommandKey("shr/DESKTOP-ABC123/sleep/set", out var key).ShouldBeTrue();
        key.ShouldBe("sleep");
    }

    [Theory]
    [InlineData("shr/OTHER-PC/sleep/set")]
    [InlineData("shr/DESKTOP-ABC123/sleep/state")]
    [InlineData("shr/DESKTOP-ABC123/a/b/set")]
    [InlineData("shr/DESKTOP-ABC123//set")]
    [InlineData("shr/DESKTOP-ABC123/commands/notification")]
    [InlineData("homeassistant/status")]
    public void TryGetCommandKey_OtherTopics_ReturnsFalse(string topic)
    {
        _topics.TryGetCommandKey(topic, out _).ShouldBeFalse();
    }

    private static MqttTopics Create()
    {
        var settings = FakeSettingsState.Configured();
        return new MqttTopics(settings.Mqtt, settings.Device);
    }
}
