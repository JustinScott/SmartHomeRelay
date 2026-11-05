using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Hosting;

/// <summary>Spec 3.3, 5.6: an entity whose command topic is an existing contract topic.</summary>
public sealed class ModuleHostCommandTopicTests : IDisposable
{
    private const string Root = "shr/DESKTOP-ABC123";

    private readonly FakeMqttTransport _transport = new();
    private readonly FakeSettingsState _settings = new(FakeSettingsState.Configured());
    private readonly ConcurrentQueue<string> _received = new();
    private readonly ModuleHost _host;

    public ModuleHostCommandTopicTests()
    {
        var notify = new EntityDescriptor("notification", "Notification", EntityComponent.Notify)
        {
            CommandTopic = t => t.NotificationCommand,
            CommandHandler = (payload, _) =>
            {
                _received.Enqueue(payload);
                return Task.CompletedTask;
            },
        };
        _host = new ModuleHost([new FakeModule("notifications", notify)], _transport, _settings, NullLoggerFactory.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task SubscribesAndRoutes_TheContractTopic_NotKeySet()
    {
        await ConnectAsync();

        _transport.Subscriptions.ShouldContain($"{Root}/commands/notification");
        _transport.Subscriptions.ShouldNotContain($"{Root}/notification/set");

        _transport.Receive($"{Root}/commands/notification", "Hello");
        _transport.Receive($"{Root}/notification/set", "Not this one");
        await _host.WhenBackgroundIdleAsync();

        _received.ShouldBe(["Hello"]);
    }

    [Fact]
    public async Task Discovery_PointsHaAtTheContractTopic()
    {
        await ConnectAsync();

        var config = _transport.PublishedTo("homeassistant/notify/DESKTOP-ABC123/notification/config").ShouldHaveSingleItem();
        config.Payload.ShouldContain($"\"command_topic\":\"{Root}/commands/notification\"");
        config.Payload.ShouldNotContain("state_topic");
    }

    [Fact]
    public void CommandTopic_OnAnEntityWithoutCommands_IsRejected()
    {
        var sensor = FakeModule.Sensor("x") with { CommandTopic = t => t.NotificationCommand };

        EntityValidator.Validate([sensor]).ShouldContain(e => e.Contains("cannot have a command topic", StringComparison.Ordinal));
    }

    private async Task ConnectAsync()
    {
        await _host.StartAsync(Ct);
        var settings = _settings.Current.Settings;
        var topics = new MqttTopics(settings.Mqtt, settings.Device);
        await _transport.ConnectAsync(new MqttConnectionOptions { Host = "h", Port = 1, ClientId = "c", WillTopic = topics.Availability }, Ct);
        await _host.OnConnectedAsync(topics, settings, Ct);
    }
}
