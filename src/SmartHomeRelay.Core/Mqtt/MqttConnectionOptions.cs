namespace SmartHomeRelay.Core.Mqtt;

/// <summary>
/// Everything needed to open one connection. A class rather than a record so the password can never
/// appear in a generated <c>ToString</c> or log line.
/// </summary>
public sealed class MqttConnectionOptions
{
    /// <summary>Broker host name or IP address.</summary>
    public required string Host { get; init; }

    /// <summary>Broker TCP port.</summary>
    public required int Port { get; init; }

    /// <summary>Client ID, <c>shr-{deviceId}</c> (spec 4.1).</summary>
    public required string ClientId { get; init; }

    /// <summary>User name, or null for anonymous access.</summary>
    public string? Username { get; init; }

    /// <summary>Decrypted password, or null. Never log this.</summary>
    public string? Password { get; init; }

    /// <summary>Last-will topic: the availability topic.</summary>
    public required string WillTopic { get; init; }

    /// <summary>Last-will payload: <c>offline</c>, retained, QoS 1.</summary>
    public string WillPayload { get; init; } = MqttTopics.Offline;

    /// <summary>MQTT keep-alive interval.</summary>
    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for the broker to accept the connection.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public override string ToString() => $"{ClientId}@{Host}:{Port}";
}
