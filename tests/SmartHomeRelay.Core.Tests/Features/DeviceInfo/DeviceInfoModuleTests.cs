using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features.DeviceInfo;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.DeviceInfo;

/// <summary>Spec 4.3: the Connected and Version sensors.</summary>
public sealed class DeviceInfoModuleTests
{
    private readonly DeviceInfoModule _module = new("1.2.3-dev+abc1234");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AlwaysOn_ConnectedAndVersion()
    {
        _module.IsEnabled(new()).ShouldBeTrue();
        _module.DescribeEntities().Select(e => e.Key).ShouldBe(["connected", "version"]);
        _module.DescribeEntities()[0].ReportsConnection.ShouldBeTrue();
    }

    [Fact]
    public async Task Start_PublishesTheVersion()
    {
        var context = new RecordingModuleContext();

        await _module.StartAsync(context, Ct);

        context.Published.ShouldBe([("version", "1.2.3-dev+abc1234")]);
    }

    [Theory]
    [InlineData("connected")]
    [InlineData("version")]
    public void Discovery_MatchesApprovedSnapshot(string key)
    {
        var settings = FakeSettingsState.Configured();
        var entity = _module.DescribeEntities().Single(e => e.Key == key);

        Snapshot.ShouldMatchApproved(DiscoveryPayloadBuilder.Build(entity, new MqttTopics(settings.Mqtt, settings.Device), settings.Device, "1.2.3"), $"device_info_{key}");
    }

    [Fact]
    public void ReportsConnection_OnlyOnAPlainBinarySensor()
    {
        var sensor = new EntityDescriptor("x", "X", EntityComponent.Sensor) { ReportsConnection = true };

        EntityValidator.Validate([sensor]).ShouldHaveSingleItem().ShouldContain("reports the connection");
    }

    [Theory]
    [InlineData("1.0.0", true, "1.0.0")]
    [InlineData("1.0.0-dev+53b3b09", false, "1.0.0-dev+53b3b09 (development build)")]
    public void Describe_MarksACopyThatIsNotInstalled(string version, bool installed, string expected) =>
        RelayVersion.Describe(version, installed).ShouldBe(expected);

    [Fact]
    public void TheBuild_StampsTheVersion()
    {
        // Directory.Build.targets: never a bare number unless it is the tagged release.
        RelayVersion.Current.ShouldStartWith(RelayVersion.Number);
    }
}
