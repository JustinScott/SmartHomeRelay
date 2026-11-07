using Microsoft.Extensions.Logging.Abstractions;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Integration;

/// <summary>
/// Remove from Home Assistant (spec 4.4) against a real Mosquitto broker: retained messages arrive on subscribe
/// and an empty retained publish deletes them. Uses <c>shrtest</c> prefixes, never a real HA's.
/// </summary>
[Trait("Category", "MqttBroker")]
public sealed class HomeAssistantRemoverBrokerTests : IAsyncLifetime
{
    private const string BaseTopic = "shrtest";
    private const string DiscoveryPrefix = "shrtest_homeassistant";

    private readonly string _deviceId = "TEST-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(MqttBrokerFixture.EnsureAvailableAsync());

    public async ValueTask DisposeAsync()
    {
        // Leave nothing behind if an assertion failed half way.
        await using var cleaner = await MqttBrokerFixture.ObserveAsync();
        foreach (var topic in Topics())
        {
            await cleaner.PublishAsync(topic, string.Empty, retain: true);
        }
    }

    [Fact]
    public async Task RetainedConfigsAndStates_AreGone_ForALateSubscriber()
    {
        var ours = $"{{\"name\":\"Uptime\",\"device\":{{\"identifiers\":[\"{_deviceId}\"]}}}}";
        var foreign = "{\"name\":\"Other\",\"device\":{\"identifiers\":[\"someone-else\"]}}";
        var (config, foreignConfig, state, status) = (Topics()[0], Topics()[1], Topics()[2], Topics()[3]);
        await using (var seed = await MqttBrokerFixture.ObserveAsync())
        {
            await seed.PublishAsync(config, ours, retain: true);
            await seed.PublishAsync(foreignConfig, foreign, retain: true);
            await seed.PublishAsync(state, "42", retain: true);
            await seed.PublishAsync(status, "offline", retain: true);
        }

        var settings = TestSettings.Valid();
        settings.Mqtt.Host = MqttBrokerFixture.Host;
        settings.Mqtt.Port = MqttBrokerFixture.Port;
        settings.Mqtt.BaseTopic = BaseTopic;
        settings.Mqtt.DiscoveryPrefix = DiscoveryPrefix;
        settings.Device.Id = _deviceId;
        var remover = new HomeAssistantRemover(() => new MqttNetTransport(), new FakeSecretProtector(), NullLogger<HomeAssistantRemover>.Instance, TimeSpan.FromMilliseconds(500));

        var result = await remover.RemoveAsync(settings, Ct);

        result.Outcome.ShouldBe(RemovalOutcome.Removed);
        result.EntitiesRemoved.ShouldBe(1);

        await using var late = await MqttBrokerFixture.ObserveAsync($"{DiscoveryPrefix}/+/{_deviceId}/#", $"{BaseTopic}/{_deviceId}/#");
        await Eventually.TrueAsync(() => late.Messages.Any(m => m.Topic == foreignConfig), "the foreign config is kept");
        late.Messages.Select(m => m.Topic).ShouldBe([foreignConfig]);
    }

    private string[] Topics() =>
    [
        $"{DiscoveryPrefix}/sensor/{_deviceId}/uptime/config",
        $"{DiscoveryPrefix}/sensor/{_deviceId}/someone_else/config",
        $"{BaseTopic}/{_deviceId}/uptime/state",
        $"{BaseTopic}/{_deviceId}/status",
    ];
}
