using SmartHomeRelay.Core.Features.Power;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Power;

/// <summary>The exact discovery configs HA receives for Power (spec 9, snapshot tests).</summary>
public sealed class PowerDiscoveryTests
{
    [Theory]
    [InlineData(PowerKeys.Shutdown)]
    [InlineData(PowerKeys.Restart)]
    [InlineData(PowerKeys.CancelShutdown)]
    [InlineData(PowerKeys.Sleep)]
    [InlineData(PowerKeys.Hibernate)]
    [InlineData(PowerKeys.Lock)]
    [InlineData(PowerKeys.SignOut)]
    [InlineData(PowerKeys.LastBoot)]
    [InlineData(PowerKeys.Uptime)]
    public void Discovery_MatchesApprovedSnapshot(string key)
    {
        var settings = FakeSettingsState.Configured();
        var power = new FakePowerController();
        var module = new PowerModule(power, power, new FakeSettingsState(settings), TimeProvider.System);
        var entity = module.DescribeEntities().Single(e => e.Key == key);

        var json = DiscoveryPayloadBuilder.Build(entity, new MqttTopics(settings.Mqtt, settings.Device), settings.Device, "1.2.3");

        Snapshot.ShouldMatchApproved(json, $"power_{key}");
    }
}
