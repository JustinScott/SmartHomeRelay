namespace SmartHomeRelay.Core.Mqtt;

/// <summary>
/// A thin wrapper over the MQTT client so connection logic can be tested with a fake.
/// It does not reconnect by itself; <see cref="MqttConnectionSupervisor"/> owns that.
/// </summary>
public interface IMqttTransport : IAsyncDisposable, IDisposable
{
    /// <summary>Raised for every message received on a subscribed topic. May be raised on any thread.</summary>
    event EventHandler<MqttMessage>? MessageReceived;

    /// <summary>Raised when an established connection ends for any reason. May be raised on any thread.</summary>
    event EventHandler? Disconnected;

    /// <summary>True while connected.</summary>
    bool IsConnected { get; }

    /// <summary>Opens a new connection with a last will. Throws if the broker cannot be reached or refuses.</summary>
    Task ConnectAsync(MqttConnectionOptions options, CancellationToken cancellationToken);

    /// <summary>Publishes a message. Throws if not connected.</summary>
    Task PublishAsync(string topic, string payload, MqttQos qos, bool retain, CancellationToken cancellationToken);

    /// <summary>Subscribes to <paramref name="topics"/> at QoS 1.</summary>
    Task SubscribeAsync(IReadOnlyCollection<string> topics, CancellationToken cancellationToken);

    /// <summary>
    /// Drops the connection without a DISCONNECT packet, so the broker publishes the last will. Used when a
    /// clean goodbye fails. Never throws; the transport can connect again afterwards.
    /// </summary>
    void Abort();

    /// <summary>
    /// Disconnects cleanly. A clean disconnect does <b>not</b> send the last will, so callers must publish
    /// <c>offline</c> first (spec 6).
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken);
}
