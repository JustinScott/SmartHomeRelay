using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features.Connection;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Connection;

/// <summary>Spec 4.3: the Connected binary sensor.</summary>
public sealed class ConnectionModuleTests
{
    private readonly ConnectionModule _module = new();

    [Fact]
    public void AlwaysOn_OneConnectivitySensor()
    {
        _module.IsEnabled(new()).ShouldBeTrue();
        var entity = _module.DescribeEntities().ShouldHaveSingleItem();
        entity.Key.ShouldBe("connected");
        entity.ReportsConnection.ShouldBeTrue();
    }

    [Fact]
    public void Discovery_ReadsTheStatusTopic_WithNoAvailability()
    {
        var settings = FakeSettingsState.Configured();
        var entity = _module.DescribeEntities().Single();

        Snapshot.ShouldMatchApproved(DiscoveryPayloadBuilder.Build(entity, new MqttTopics(settings.Mqtt, settings.Device), settings.Device, "1.2.3"), "connection_connected");
    }

    [Fact]
    public void ReportsConnection_OnlyOnAPlainBinarySensor()
    {
        var sensor = new EntityDescriptor("x", "X", EntityComponent.Sensor) { ReportsConnection = true };

        EntityValidator.Validate([sensor]).ShouldHaveSingleItem().ShouldContain("reports the connection");
    }
}
