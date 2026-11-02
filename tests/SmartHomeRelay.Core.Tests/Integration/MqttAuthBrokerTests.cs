using Microsoft.Extensions.Logging.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Integration;

/// <summary>
/// Against a broker that requires credentials, like the Home Assistant Mosquitto add-on.
/// Regression: MQTTnet v5 does not throw when the broker refuses a connection, so a refused connection
/// was treated as connected.
/// </summary>
[Trait("Category", "MqttBroker")]
public sealed class MqttAuthBrokerTests : IAsyncLifetime
{
    private readonly string _deviceId = "TEST-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    public async ValueTask InitializeAsync() => await MqttBrokerFixture.EnsureAuthBrokerAvailableAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Connect_CorrectCredentials_Connects()
    {
        await using var transport = new MqttNetTransport();

        await transport.ConnectAsync(Options(MqttBrokerFixture.AuthUser, MqttBrokerFixture.AuthPassword), TestContext.Current.CancellationToken);

        transport.IsConnected.ShouldBeTrue();
        await transport.DisconnectAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("relay", "wrong-password")]
    public async Task Connect_RefusedByBroker_Throws(string? user, string? password)
    {
        await using var transport = new MqttNetTransport();

        var ex = await Should.ThrowAsync<MqttConnectionRefusedException>(
            () => transport.ConnectAsync(Options(user, password), TestContext.Current.CancellationToken));

        ex.IsAuthenticationFailure.ShouldBeTrue();
        transport.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task Supervisor_WrongPassword_ReportsErrorInsteadOfConnected()
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.Host = MqttBrokerFixture.Host;
        settings.Mqtt.Port = MqttBrokerFixture.AuthPort;
        settings.Mqtt.Username = MqttBrokerFixture.AuthUser;
        settings.Mqtt.Password = "wrong-password";
        settings.Mqtt.BaseTopic = "shrtest";
        settings.Mqtt.DiscoveryPrefix = "shrtest_homeassistant";
        settings.Device.Id = _deviceId;
        await using var transport = new MqttNetTransport();
        await using var supervisor = new MqttConnectionSupervisor(
            transport,
            new NoopSessionHandler(),
            new FakeSettingsState(settings),
            new FakeSecretProtector(),
            new ReconnectBackoff(new Random()),
            TimeProvider.System,
            NullLogger<MqttConnectionSupervisor>.Instance);

        await supervisor.StartAsync(TestContext.Current.CancellationToken);

        await Eventually.TrueAsync(() => supervisor.Status == RelayStatus.Error, "the refusal is reported as an error");
        transport.IsConnected.ShouldBeFalse();
        await supervisor.StopAsync(TestContext.Current.CancellationToken);
    }

    private MqttConnectionOptions Options(string? user, string? password) => new()
    {
        Host = MqttBrokerFixture.Host,
        Port = MqttBrokerFixture.AuthPort,
        ClientId = "shr-" + _deviceId,
        Username = user,
        Password = password,
        WillTopic = $"shrtest/{_deviceId}/status",
    };

    private sealed class NoopSessionHandler : IMqttSessionHandler
    {
        public Task OnConnectedAsync(MqttTopics topics, AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
