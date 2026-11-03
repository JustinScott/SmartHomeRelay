using SmartHomeRelay.Core.Features.Presence;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Presence;

/// <summary>
/// The exact discovery configs HA receives for Presence (spec 9, snapshot tests). A change here changes
/// what users see in HA, so it must be deliberate.
/// </summary>
public sealed class PresenceDiscoveryTests
{
    [Theory]
    [InlineData(PresenceKeys.SessionLocked)]
    [InlineData(PresenceKeys.LoggedInUser)]
    [InlineData(PresenceKeys.IdleSeconds)]
    [InlineData(PresenceKeys.UserActive)]
    [InlineData(PresenceKeys.PresenceState)]
    public void Discovery_MatchesApprovedSnapshot(string key)
    {
        var settings = FakeSettingsState.Configured();
        var module = new PresenceModule(new FakeSystemEventSource(), new FakePresenceSources(), new FakePresenceSources(), new FakeSettingsState(settings), TimeProvider.System);
        var entity = module.DescribeEntities().Single(e => e.Key == key);

        var json = DiscoveryPayloadBuilder.Build(entity, new MqttTopics(settings.Mqtt, settings.Device), settings.Device, "1.2.3");

        Snapshot.ShouldMatchApproved(json, $"presence_{key}");
    }
}
