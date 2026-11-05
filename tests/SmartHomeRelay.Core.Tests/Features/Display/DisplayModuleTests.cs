using SmartHomeRelay.Core.Features.Display;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Display;

public sealed class DisplayModuleTests
{
    private readonly FakeDisplayController _display = new();
    private readonly DisplayModule _module;

    public DisplayModuleTests()
    {
        _module = new DisplayModule(_display);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void DescribeEntities_TwoButtons_NoStateSensor()
    {
        // Spec 5.3: Windows can't reliably report monitor state.
        _module.DescribeEntities().Select(e => e.Key).ShouldBe(["monitor_off", "monitor_on"]);
    }

    [Theory]
    [InlineData("monitor_off", "off")]
    [InlineData("monitor_on", "on")]
    public async Task Press_CallsTheController(string key, string call)
    {
        await _module.StartAsync(new RecordingModuleContext(), Ct);

        await Handler(key)("PRESS", Ct);

        _display.Calls.ShouldBe([call]);
    }

    [Fact]
    public async Task OtherPayload_IsIgnored()
    {
        await _module.StartAsync(new RecordingModuleContext(), Ct);

        await Handler("monitor_off")("ON", Ct);

        _display.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void IsEnabled_FollowsDisplayToggle()
    {
        var settings = FakeSettingsState.Configured();
        _module.IsEnabled(settings).ShouldBeTrue();

        settings.Features.Display.Enabled = false;
        _module.IsEnabled(settings).ShouldBeFalse();
    }

    [Theory]
    [InlineData("monitor_off")]
    [InlineData("monitor_on")]
    public void Discovery_MatchesApprovedSnapshot(string key)
    {
        var settings = FakeSettingsState.Configured();
        var entity = _module.DescribeEntities().Single(e => e.Key == key);

        Snapshot.ShouldMatchApproved(DiscoveryPayloadBuilder.Build(entity, new MqttTopics(settings.Mqtt, settings.Device), settings.Device, "1.2.3"), $"display_{key}");
    }

    private Core.Entities.EntityCommandHandler Handler(string key) => _module.DescribeEntities().Single(e => e.Key == key).CommandHandler!;
}
