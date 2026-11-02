using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace SmartHomeRelay.Core.Mqtt;

/// <summary>
/// <see cref="IMqttTransport"/> over MQTTnet's plain <see cref="IMqttClient"/>. A fresh client is built
/// for every connection, as the prototype did after resume, so no state leaks between connections.
/// </summary>
public sealed class MqttNetTransport : IMqttTransport
{
    private readonly MqttClientFactory _factory = new();
    private readonly Lock _gate = new();
    private IMqttClient? _client;
    private Func<MqttClientDisconnectedEventArgs, Task>? _disconnectedHandler;

    /// <inheritdoc />
    public event EventHandler<MqttMessage>? MessageReceived;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <inheritdoc />
    public bool IsConnected => CurrentClient?.IsConnected ?? false;

    private IMqttClient? CurrentClient
    {
        get
        {
            lock (_gate)
            {
                return _client;
            }
        }
    }

    /// <inheritdoc />
    public async Task ConnectAsync(MqttConnectionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        DisposeClient();

        var client = _factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        // Only the current client may report a disconnect; a late event from a replaced client must not
        // make the supervisor think the new connection dropped.
        Task DisconnectedHandler(MqttClientDisconnectedEventArgs e) => OnDisconnectedAsync(client, e);
        client.DisconnectedAsync += DisconnectedHandler;

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(options.Host, options.Port)
            .WithClientId(options.ClientId)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession()
            .WithKeepAlivePeriod(options.KeepAlive)
            .WithTimeout(options.ConnectTimeout)
            .WithWillTopic(options.WillTopic)
            .WithWillPayload(options.WillPayload)
            .WithWillRetain()
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        if (!string.IsNullOrEmpty(options.Username))
        {
            builder = builder.WithCredentials(options.Username, options.Password ?? string.Empty);
        }

        lock (_gate)
        {
            _client = client;
            _disconnectedHandler = DisconnectedHandler;
        }

        // Throws for socket and timeout failures. A refusal by the broker (e.g. bad credentials) does NOT
        // throw in MQTTnet v5; it comes back as a result code, so it must be checked here.
        var result = await client.ConnectAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
        if (result.ResultCode != MqttClientConnectResultCode.Success || !client.IsConnected)
        {
            DisposeClient();
            var isAuthenticationFailure = result.ResultCode is MqttClientConnectResultCode.BadUserNameOrPassword
                or MqttClientConnectResultCode.NotAuthorized;
            throw new MqttConnectionRefusedException(result.ResultCode.ToString(), isAuthenticationFailure);
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync(string topic, string payload, MqttQos qos, bool retain, CancellationToken cancellationToken)
    {
        var client = CurrentClient ?? throw new InvalidOperationException("Not connected.");
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)qos)
            .WithRetainFlag(retain)
            .Build();
        await client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SubscribeAsync(IReadOnlyCollection<string> topics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(topics);
        if (topics.Count == 0)
        {
            return;
        }

        var client = CurrentClient ?? throw new InvalidOperationException("Not connected.");
        var builder = _factory.CreateSubscribeOptionsBuilder();
        foreach (var topic in topics)
        {
            builder = builder.WithTopicFilter(f => f.WithTopic(topic).WithAtLeastOnceQoS());
        }

        await client.SubscribeAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        var client = CurrentClient;
        if (client is { IsConnected: true })
        {
            // MQTT 3.1.1 has no disconnect reasons; default options send a plain DISCONNECT.
            await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Abort() => DisposeClient();

    /// <inheritdoc />
    public void Dispose() => DisposeClient();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        DisposeClient();
        return ValueTask.CompletedTask;
    }

    private void DisposeClient()
    {
        IMqttClient? client;
        Func<MqttClientDisconnectedEventArgs, Task>? disconnectedHandler;
        lock (_gate)
        {
            client = _client;
            disconnectedHandler = _disconnectedHandler;
            _client = null;
            _disconnectedHandler = null;
        }

        if (client is null)
        {
            return;
        }

        client.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
        if (disconnectedHandler is not null)
        {
            client.DisconnectedAsync -= disconnectedHandler;
        }

        // Disposing without a DISCONNECT packet closes the socket uncleanly, so if the connection was still
        // live the broker publishes the last will (offline). MQTT 3.1.1 has no other way to ask for it.
        // Normal shutdown publishes offline and disconnects cleanly before reaching this.
        client.Dispose();
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var message = e.ApplicationMessage;
        MessageReceived?.Invoke(this, new MqttMessage(message.Topic, message.ConvertPayloadToString() ?? string.Empty));
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(IMqttClient client, MqttClientDisconnectedEventArgs e)
    {
        if (e.ClientWasConnected && ReferenceEquals(client, CurrentClient))
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }
}
