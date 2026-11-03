using SmartHomeRelay.Core.Features.Metrics;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Metrics;

/// <summary>The exact discovery configs HA receives for Metrics (spec 9, snapshot tests).</summary>
public sealed class MetricsDiscoveryTests
{
    [Theory]
    [InlineData("cpu_usage")]
    [InlineData("memory_usage")]
    [InlineData("memory_used_gb")]
    [InlineData("disk_usage_c")]
    [InlineData("disk_free_c")]
    [InlineData("network_down")]
    [InlineData("network_up")]
    [InlineData("gpu_usage")]
    [InlineData("cpu_power")]
    [InlineData("gpu_temperature")]
    [InlineData("gpu_power_level")]
    public void Discovery_MatchesApprovedSnapshot(string key)
    {
        var settings = FakeSettingsState.Configured();
        var module = new MetricsModule(new FakeMetricsSource(), new FakeSettingsState(settings), TimeProvider.System);
        var entity = module.DescribeEntities().Single(e => e.Key == key);

        var json = DiscoveryPayloadBuilder.Build(entity, new MqttTopics(settings.Mqtt, settings.Device), settings.Device, "1.2.3");

        Snapshot.ShouldMatchApproved(json, $"metrics_{key}");
    }
}
