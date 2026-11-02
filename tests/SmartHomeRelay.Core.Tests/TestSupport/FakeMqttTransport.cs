using System.Collections.Concurrent;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>In-memory transport that records every call in order.</summary>
internal sealed class FakeMqttTransport : IMqttTransport
{
    private readonly ConcurrentQueue<Exception> _connectFailures = new();
    private volatile bool _connected;

    public event EventHandler<MqttMessage>? MessageReceived;

    public event EventHandler? Disconnected;

    public bool IsConnected => _connected;

    /// <summary>Every call, in order: "connect", "publish", "subscribe", "disconnect".</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    public ConcurrentQueue<Published> Publications { get; } = new();

    public ConcurrentQueue<string> Subscriptions { get; } = new();

    public ConcurrentQueue<MqttConnectionOptions> ConnectAttempts { get; } = new();

    public int DisconnectCount { get; private set; }

    /// <summary>Connect attempts wait until cancelled, like an unreachable broker before its timeout.</summary>
    public bool HangConnects { get; set; }

    /// <summary>Publishes wait until cancelled, like a stalled network.</summary>
    public bool HangPublishes { get; set; }

    public void FailNextConnects(int count, Exception? exception = null)
    {
        for (var i = 0; i < count; i++)
        {
            _connectFailures.Enqueue(exception ?? new IOException("Broker unreachable."));
        }
    }

    public async Task ConnectAsync(MqttConnectionOptions options, CancellationToken cancellationToken)
    {
        ConnectAttempts.Enqueue(options);
        Calls.Enqueue("connect");
        if (HangConnects)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (_connectFailures.TryDequeue(out var failure))
        {
            throw failure;
        }

        _connected = true;
    }

    public async Task PublishAsync(string topic, string payload, MqttQos qos, bool retain, CancellationToken cancellationToken)
    {
        if (!_connected)
        {
            throw new InvalidOperationException("Not connected.");
        }

        if (HangPublishes)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        Calls.Enqueue($"publish {topic} {payload}");
        Publications.Enqueue(new Published(topic, payload, qos, retain));
    }

    public Task SubscribeAsync(IReadOnlyCollection<string> topics, CancellationToken cancellationToken)
    {
        Calls.Enqueue("subscribe");
        foreach (var topic in topics)
        {
            Subscriptions.Enqueue(topic);
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        Calls.Enqueue("disconnect");
        DisconnectCount++;
        _connected = false;
        return Task.CompletedTask;
    }

    public void Abort()
    {
        Calls.Enqueue("abort");
        _connected = false;
    }

    /// <summary>Simulates the broker or network dropping the connection.</summary>
    public void Drop()
    {
        _connected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Simulates a message arriving from the broker.</summary>
    public void Receive(string topic, string payload) => MessageReceived?.Invoke(this, new MqttMessage(topic, payload));

    public List<Published> PublishedTo(string topic) => [.. Publications.Where(p => p.Topic == topic)];

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose()
    {
    }

    internal sealed record Published(string Topic, string Payload, MqttQos Qos, bool Retain);
}
