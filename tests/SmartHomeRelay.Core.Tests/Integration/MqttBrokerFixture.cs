using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using MQTTnet;

namespace SmartHomeRelay.Core.Tests.Integration;

/// <summary>
/// Locates the test broker: <c>SHR_TEST_MQTT_HOST</c> / <c>SHR_TEST_MQTT_PORT</c> (default localhost:1883).
/// Tests skip when no broker answers, unless <c>SHR_REQUIRE_MQTT_BROKER=1</c> (set in CI), where a
/// missing broker is a failure. Never point this at your Home Assistant broker.
/// </summary>
internal static class MqttBrokerFixture
{
    public static string Host { get; } = Environment.GetEnvironmentVariable("SHR_TEST_MQTT_HOST") is { Length: > 0 } host ? host : "localhost";

    public static int Port { get; } = int.TryParse(Environment.GetEnvironmentVariable("SHR_TEST_MQTT_PORT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 1883;

    private static bool Required => Environment.GetEnvironmentVariable("SHR_REQUIRE_MQTT_BROKER") == "1";

    public static async Task EnsureAvailableAsync()
    {
        try
        {
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await tcp.ConnectAsync(Host, Port, timeout.Token);
            return;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            var message = $"No MQTT broker at {Host}:{Port}. Start Mosquitto or set SHR_TEST_MQTT_HOST.";
            if (Required)
            {
                Assert.Fail(message);
            }

            Assert.Skip(message);
        }
    }

    /// <summary>A plain client that records everything it receives, including retained messages.</summary>
    public static async Task<Observer> ObserveAsync(params string[] topicFilters)
    {
        var factory = new MqttClientFactory();
        var client = factory.CreateMqttClient();
        var observer = new Observer(client);
        client.ApplicationMessageReceivedAsync += e =>
        {
            observer.Messages.Enqueue((e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString() ?? string.Empty, e.ApplicationMessage.Retain));
            return Task.CompletedTask;
        };

        await client.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(Host, Port).WithClientId("shrtest-observer-" + Guid.NewGuid().ToString("N")).Build());
        if (topicFilters.Length == 0)
        {
            return observer; // publish-only client
        }

        var subscribe = factory.CreateSubscribeOptionsBuilder();
        foreach (var filter in topicFilters)
        {
            subscribe = subscribe.WithTopicFilter(f => f.WithTopic(filter).WithAtLeastOnceQoS());
        }

        await client.SubscribeAsync(subscribe.Build());
        return observer;
    }

    internal sealed class Observer(IMqttClient client) : IAsyncDisposable
    {
        public ConcurrentQueue<(string Topic, string Payload, bool Retained)> Messages { get; } = new();

        public bool Saw(string topic, string payload) => Messages.Any(m => m.Topic == topic && m.Payload == payload);

        public Task PublishAsync(string topic, string payload, bool retain = false) =>
            client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag(retain).WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build());

        public async ValueTask DisposeAsync()
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync();
            }

            client.Dispose();
        }
    }
}
