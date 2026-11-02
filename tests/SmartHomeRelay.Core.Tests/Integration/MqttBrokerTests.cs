using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Integration;

/// <summary>
/// The full connection stack against a real Mosquitto broker (spec 9, Integration (MQTT)).
/// Uses <c>shrtest</c> topic prefixes so it can never create devices in a real Home Assistant.
/// </summary>
[Trait("Category", "MqttBroker")]
public sealed class MqttBrokerTests : IAsyncLifetime
{
    private const string BaseTopic = "shrtest";
    private const string DiscoveryPrefix = "shrtest_homeassistant";

    private readonly string _deviceId = "TEST-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private readonly ConcurrentQueue<string> _presses = new();
    private readonly List<IAsyncDisposable> _cleanup = [];
    private MqttTopics _topics = null!;

    public async ValueTask InitializeAsync()
    {
        await MqttBrokerFixture.EnsureAvailableAsync();
        var settings = Settings();
        _topics = new MqttTopics(settings.Mqtt, settings.Device);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in Enumerable.Reverse(_cleanup))
        {
            await item.DisposeAsync();
        }

        if (_topics is not null)
        {
            // Remove the retained messages this test left on the broker.
            await using var cleaner = await MqttBrokerFixture.ObserveAsync();
            foreach (var topic in new[]
            {
                _topics.Availability,
                _topics.Discovery(EntityComponent.Sensor, "uptime"),
                _topics.Discovery(EntityComponent.Button, "sleep"),
                _topics.State("uptime"),
            })
            {
                await cleaner.PublishAsync(topic, string.Empty, retain: true);
            }
        }
    }

    [Fact]
    public async Task Connect_PublishesRetainedOnlineAndDiscovery()
    {
        var (supervisor, _, _) = await StartStackAsync();
        await Eventually.TrueAsync(() => supervisor.Status == RelayStatus.Connected, "connected to the broker");

        // A late subscriber still gets them, which proves they are retained.
        await using var observer = await ObserveDeviceAsync();
        await Eventually.TrueAsync(() => observer.Saw(_topics.Availability, "online"), "retained online is received");
        await Eventually.TrueAsync(
            () => observer.Messages.Any(m => m.Topic == _topics.Discovery(EntityComponent.Sensor, "uptime") && m.Retained && m.Payload.Contains("\"manufacturer\":\"Smart Home Relay\"", StringComparison.Ordinal)),
            "retained discovery config is received");
    }

    [Fact]
    public async Task State_PublishedBeforeConnect_IsDeliveredAfterConnect()
    {
        var (supervisor, _, module) = await StartStackAsync();
        await module.Context!.PublishStateAsync("uptime", "42", TestContext.Current.CancellationToken);
        await Eventually.TrueAsync(() => supervisor.Status == RelayStatus.Connected, "connected to the broker");

        await using var observer = await ObserveDeviceAsync();
        await Eventually.TrueAsync(() => observer.Saw(_topics.State("uptime"), "42"), "the cached state is published");
    }

    [Fact]
    public async Task Command_FromBroker_ReachesEntityHandler()
    {
        var (supervisor, _, _) = await StartStackAsync();
        await Eventually.TrueAsync(() => supervisor.Status == RelayStatus.Connected, "connected to the broker");
        await using var observer = await ObserveDeviceAsync();
        await Eventually.TrueAsync(() => observer.Saw(_topics.Availability, "online"), "observer is ready");

        await observer.PublishAsync(_topics.Command("sleep"), "PRESS");

        await Eventually.TrueAsync(() => _presses.Contains("PRESS"), "the button handler receives the command");
    }

    [Fact]
    public async Task Stop_PublishesRetainedOffline()
    {
        var (supervisor, _, _) = await StartStackAsync();
        await Eventually.TrueAsync(() => supervisor.Status == RelayStatus.Connected, "connected to the broker");
        await using var observer = await ObserveDeviceAsync();

        await supervisor.StopAsync(TestContext.Current.CancellationToken);

        await Eventually.TrueAsync(() => observer.Saw(_topics.Availability, "offline"), "offline is published");
        await using var late = await ObserveDeviceAsync();
        await Eventually.TrueAsync(() => late.Saw(_topics.Availability, "offline"), "offline is retained");
    }

    [Fact]
    public async Task CleanDisconnect_OverMqtt311_SucceedsWithoutLastWill()
    {
        // Regression: MQTT 3.1.1 has no disconnect reasons; sending one throws. A clean disconnect must
        // work and must not trigger the will (which is why offline is published first).
        await using var transport = new MqttNetTransport();
        await transport.ConnectAsync(
            new MqttConnectionOptions { Host = MqttBrokerFixture.Host, Port = MqttBrokerFixture.Port, ClientId = "shr-" + _deviceId, WillTopic = _topics.Availability },
            TestContext.Current.CancellationToken);
        await using var observer = await ObserveDeviceAsync();

        await transport.DisconnectAsync(TestContext.Current.CancellationToken);
        transport.IsConnected.ShouldBeFalse();

        // Prove the will was not sent: anything published after the disconnect arrives, and no offline came before it.
        await observer.PublishAsync(_topics.State("uptime"), "marker");
        await Eventually.TrueAsync(() => observer.Saw(_topics.State("uptime"), "marker"), "the marker arrives");
        observer.Saw(_topics.Availability, "offline").ShouldBeFalse();
    }

    [Fact]
    public async Task UncleanDisconnect_BrokerPublishesLastWill()
    {
        var transport = new MqttNetTransport();
        await transport.ConnectAsync(
            new MqttConnectionOptions { Host = MqttBrokerFixture.Host, Port = MqttBrokerFixture.Port, ClientId = "shr-" + _deviceId, WillTopic = _topics.Availability },
            TestContext.Current.CancellationToken);
        await transport.PublishAsync(_topics.Availability, "online", MqttQos.AtLeastOnce, retain: true, TestContext.Current.CancellationToken);
        await using var observer = await ObserveDeviceAsync();
        await Eventually.TrueAsync(() => observer.Saw(_topics.Availability, "online"), "online is seen");

        // Abandoning a live connection asks the broker to send the will, as a crash or network loss would.
        await transport.DisposeAsync();

        await Eventually.TrueAsync(() => observer.Saw(_topics.Availability, "offline"), "the last will (offline) is published by the broker");
    }

    [Fact]
    public async Task ConnectionTakenOver_ReconnectsAndRepublishesOnline()
    {
        var (supervisor, _, _) = await StartStackAsync();
        await Eventually.TrueAsync(() => supervisor.Status == RelayStatus.Connected, "connected to the broker");
        await using var observer = await ObserveDeviceAsync();
        await Eventually.TrueAsync(() => observer.Saw(_topics.Availability, "online"), "observer is ready");
        observer.Messages.Clear();

        // A second client with the same client ID makes the broker drop ours: a real connection loss.
        using var intruder = new MqttClientFactory().CreateMqttClient();
        try
        {
            await intruder.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(MqttBrokerFixture.Host, MqttBrokerFixture.Port).WithClientId("shr-" + _deviceId).Build(), TestContext.Current.CancellationToken);
        }
        catch (MQTTnet.Exceptions.MqttCommunicationException)
        {
            // Our app may reconnect and take the client ID back before the intruder's handshake finishes.
            // The intruder only exists to force the disconnect, so its own result doesn't matter.
        }

        await Eventually.TrueAsync(() => observer.Saw(_topics.Availability, "online"), "it reconnects (after ~1s backoff) and republishes online");
        // The observer can see "online" just before our publish call returns and the status flips.
        await Eventually.TrueAsync(() => supervisor.Status == RelayStatus.Connected, "status returns to Connected");
    }

    private AppSettings Settings()
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.Host = MqttBrokerFixture.Host;
        settings.Mqtt.Port = MqttBrokerFixture.Port;
        settings.Mqtt.BaseTopic = BaseTopic;
        settings.Mqtt.DiscoveryPrefix = DiscoveryPrefix;
        settings.Device.Id = _deviceId;
        settings.Device.Name = "Integration test";
        return settings;
    }

    private async Task<(MqttConnectionSupervisor Supervisor, ModuleHost Host, FakeModule Module)> StartStackAsync()
    {
        var settings = new FakeSettingsState(Settings());
        var transport = new MqttNetTransport();
        var module = new FakeModule("power", FakeModule.Sensor("uptime"), FakeModule.Button("sleep", _presses));
        var host = new ModuleHost([module], transport, settings, NullLoggerFactory.Instance);
        var supervisor = new MqttConnectionSupervisor(
            transport,
            host,
            settings,
            new FakeSecretProtector(),
            new ReconnectBackoff(new Random()),
            TimeProvider.System,
            NullLogger<MqttConnectionSupervisor>.Instance);

        // Disposed in reverse: stop (offline + disconnect), then dispose the supervisor, then the transport.
        _cleanup.Add(transport);
        _cleanup.Add(supervisor);
        _cleanup.Add(new AsyncDisposableAction(async () =>
        {
            await supervisor.StopAsync(CancellationToken.None);
            await host.StopAsync(CancellationToken.None);
            host.Dispose();
        }));

        await host.StartAsync(TestContext.Current.CancellationToken);
        await supervisor.StartAsync(TestContext.Current.CancellationToken);
        return (supervisor, host, module);
    }

    private Task<MqttBrokerFixture.Observer> ObserveDeviceAsync() =>
        MqttBrokerFixture.ObserveAsync($"{BaseTopic}/{_deviceId}/#", $"{DiscoveryPrefix}/+/{_deviceId}/#");

    private sealed class AsyncDisposableAction(Func<Task> action) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await action();
    }
}
