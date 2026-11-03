using SmartHomeRelay.Core.Features.Hardware;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Hardware;

/// <summary>The exact discovery configs HA receives for hardware sensors (spec 5.8).</summary>
public sealed class HardwareDiscoveryTests
{
    [Theory]
    [InlineData("cpu_temperature")]
    [InlineData("cpu_fan")]
    [InlineData("ssd_temperature")]
    [InlineData("hardware_status")]
    [InlineData("lhm_version")]
    public void Discovery_MatchesApprovedSnapshot(string key)
    {
        var settings = FakeSettingsState.Configured();
        var module = new HardwareModule(new FakeLhmEndpoint(), new FakeSettingsState(settings), new FakeSecretProtector(), new FakeUserNotifier(), TimeProvider.System);
        var entity = module.DescribeEntities().Single(e => e.Key == key);

        var json = DiscoveryPayloadBuilder.Build(entity, new MqttTopics(settings.Mqtt, settings.Device), settings.Device, "1.2.3");

        Snapshot.ShouldMatchApproved(json, $"hardware_{key}");
    }
}
