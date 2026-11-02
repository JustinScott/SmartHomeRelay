using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Mqtt;

public sealed class MqttConnectionSupervisorTests : IAsyncDisposable
{
    private const string Availability = "shr/DESKTOP-ABC123/status";

    private readonly FakeMqttTransport _transport = new();
    private readonly FakeSettingsState _settings = new(FakeSettingsState.Configured());
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingSessionHandler _handler = new();
    private readonly MqttConnectionSupervisor _supervisor;

    public MqttConnectionSupervisorTests()
    {
        _supervisor = new MqttConnectionSupervisor(
            _transport,
            _handler,
            _settings,
            new FakeSecretProtector(),
            new ReconnectBackoff(new NoJitterRandom()),
            _time,
            NullLogger<MqttConnectionSupervisor>.Instance);
    }

    public async ValueTask DisposeAsync() => await _supervisor.DisposeAsync();

    [Fact]
    public async Task Start_Configured_ConnectsWithRetainedOfflineLastWill()
    {
        await StartAndWaitForStatusAsync(RelayStatus.Connected);

        var options = _transport.ConnectAttempts.ShouldHaveSingleItem();
        options.Host.ShouldBe("broker.local");
        options.Port.ShouldBe(1883);
        options.ClientId.ShouldBe("shr-DESKTOP-ABC123");
        options.WillTopic.ShouldBe(Availability);
        options.WillPayload.ShouldBe("offline");
    }

    [Fact]
    public async Task Start_Connected_PublishesRetainedOnlineThenHandsOverSession()
    {
        await StartAndWaitForStatusAsync(RelayStatus.Connected);
        await Eventually.TrueAsync(() => _handler.Sessions.Count == 1, "the session handler is called");

        var online = _transport.PublishedTo(Availability).ShouldHaveSingleItem();
        online.Payload.ShouldBe("online");
        online.Retain.ShouldBeTrue();
        online.Qos.ShouldBe(MqttQos.AtLeastOnce);
        _handler.Sessions.Single().Topics.Availability.ShouldBe(Availability);
    }

    [Fact]
    public async Task Start_NotConfigured_DoesNotConnectUntilHostIsSet()
    {
        _settings.Set(TestSettings.Valid());

        await StartAndWaitForStatusAsync(RelayStatus.NotConfigured);
        _transport.ConnectAttempts.ShouldBeEmpty();

        _settings.Set(FakeSettingsState.Configured());
        await Eventually.TrueAsync(() => _supervisor.Status == RelayStatus.Connected, "it connects once configured");
    }

    [Fact]
    public async Task Start_InvalidSettings_ReportsSettingsInvalidAndDoesNotConnect()
    {
        _settings.Set(FakeSettingsState.Configured(), ["mqtt.port must be between 1 and 65535."]);

        await StartAndWaitForStatusAsync(RelayStatus.SettingsInvalid);

        _transport.ConnectAttempts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConnectFails_RetriesAfterBackoffWithoutCrashing()
    {
        _transport.FailNextConnects(1);

        await StartAndWaitForStatusAsync(RelayStatus.Reconnecting);
        _transport.ConnectAttempts.Count.ShouldBe(1);

        await Eventually.TrueAsync(
            () => _supervisor.Status == RelayStatus.Connected,
            "the retry succeeds after the backoff delay",
            betweenChecks: () => _time.Advance(TimeSpan.FromMilliseconds(500)));
        _transport.ConnectAttempts.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ConnectRefused_BadCredentials_ReportsErrorAndKeepsRetrying()
    {
        _transport.FailNextConnects(1, new MqttConnectionRefusedException("NotAuthorized", isAuthenticationFailure: true));

        await StartAndWaitForStatusAsync(RelayStatus.Error);

        await Eventually.TrueAsync(
            () => _supervisor.Status == RelayStatus.Connected,
            "the retry succeeds once the broker accepts",
            betweenChecks: () => _time.Advance(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task ConnectRefused_OtherReason_ReportsReconnecting()
    {
        _transport.FailNextConnects(1, new MqttConnectionRefusedException("ServerUnavailable", isAuthenticationFailure: false));

        await StartAndWaitForStatusAsync(RelayStatus.Reconnecting);
    }

    [Fact]
    public async Task ConnectionDropped_ReconnectsAndRepublishesOnline()
    {
        await StartAndWaitForStatusAsync(RelayStatus.Connected);

        _transport.Drop();
        await Eventually.TrueAsync(() => _supervisor.Status == RelayStatus.Reconnecting, "the drop is noticed");
        await Eventually.TrueAsync(
            () => _supervisor.Status == RelayStatus.Connected,
            "it reconnects",
            betweenChecks: () => _time.Advance(TimeSpan.FromMilliseconds(500)));

        _transport.ConnectAttempts.Count.ShouldBe(2);
        _transport.PublishedTo(Availability).Count(p => p.Payload == "online").ShouldBe(2);
        await Eventually.TrueAsync(() => _handler.Sessions.Count == 2, "the session is set up again (full state publish)");
    }

    [Fact]
    public async Task Stop_WhileConnected_PublishesOfflineBeforeDisconnecting()
    {
        // Regression (spec 3.5 #1): a clean disconnect doesn't trigger the last will, so without this
        // HA would show the PC as online while it sleeps.
        await StartAndWaitForStatusAsync(RelayStatus.Connected);

        await _supervisor.StopAsync(TestContext.Current.CancellationToken);

        var calls = _transport.Calls.ToList();
        calls[^1].ShouldBe("disconnect");
        calls[^2].ShouldBe($"publish {Availability} offline");
        var offline = _transport.PublishedTo(Availability).Last();
        offline.Retain.ShouldBeTrue();
        offline.Qos.ShouldBe(MqttQos.AtLeastOnce);
        _supervisor.Status.ShouldBe(RelayStatus.Disconnected);
    }

    [Fact]
    public async Task SettingsChanged_NewHost_GoesOfflineThenConnectsToNewHost()
    {
        await StartAndWaitForStatusAsync(RelayStatus.Connected);

        _settings.Set(FakeSettingsState.Configured("new-broker.local"));
        await Eventually.TrueAsync(() => _transport.ConnectAttempts.Count == 2, "it reconnects");

        _transport.ConnectAttempts.Last().Host.ShouldBe("new-broker.local");
        var calls = _transport.Calls.ToList();
        var disconnectIndex = calls.IndexOf("disconnect");
        calls[disconnectIndex - 1].ShouldBe($"publish {Availability} offline");
    }

    [Fact]
    public async Task SettingsChanged_FeatureToggleOnly_DoesNotReconnect()
    {
        await StartAndWaitForStatusAsync(RelayStatus.Connected);

        var settings = FakeSettingsState.Configured();
        settings.Features.Metrics.Enabled = false;
        _settings.Set(settings);

        // The settings handler runs synchronously, so a wake would be queued (or already consumed) by now.
        _supervisor.HasPendingWake.ShouldBeFalse();

        // A real reconnect afterwards: if the toggle had also woken the loop there would be two.
        _supervisor.RequestReconnect();
        await Eventually.TrueAsync(() => _transport.ConnectAttempts.Count == 2, "the requested reconnect happens");
        _transport.DisconnectCount.ShouldBe(1);
    }

    [Fact]
    public async Task RequestReconnect_GoesOfflineAndReconnectsImmediately()
    {
        await StartAndWaitForStatusAsync(RelayStatus.Connected);

        _supervisor.RequestReconnect();

        await Eventually.TrueAsync(() => _transport.ConnectAttempts.Count == 2, "it reconnects without waiting for backoff");
        _transport.PublishedTo(Availability).Select(p => p.Payload).ShouldBe(["online", "offline", "online"]);
    }

    [Fact]
    public async Task Connect_PlainPasswordInSettings_IsUsed()
    {
        var settings = FakeSettingsState.Configured();
        settings.Mqtt.Username = "relay";
        settings.Mqtt.Password = "typed-by-hand";
        _settings.Set(settings);

        await StartAndWaitForStatusAsync(RelayStatus.Connected);

        var options = _transport.ConnectAttempts.Single();
        options.Username.ShouldBe("relay");
        options.Password.ShouldBe("typed-by-hand");
    }

    [Fact]
    public async Task Connect_ProtectedPassword_IsDecrypted()
    {
        var settings = FakeSettingsState.Configured();
        settings.Mqtt.Username = "relay";
        settings.Mqtt.PasswordProtected = new FakeSecretProtector().Protect("secret");
        _settings.Set(settings);

        await StartAndWaitForStatusAsync(RelayStatus.Connected);

        _transport.ConnectAttempts.Single().Password.ShouldBe("secret");
    }

    [Fact]
    public async Task Connect_PasswordCannotBeDecrypted_ReportsErrorAndDoesNotConnect()
    {
        var settings = FakeSettingsState.Configured();
        settings.Mqtt.PasswordProtected = "blob-from-another-pc";
        _settings.Set(settings);

        await StartAndWaitForStatusAsync(RelayStatus.Error);

        _transport.ConnectAttempts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Connect_NoUsername_ConnectsAnonymously()
    {
        await StartAndWaitForStatusAsync(RelayStatus.Connected);

        _transport.ConnectAttempts.Single().Username.ShouldBeNull();
    }

    [Fact]
    public async Task SessionHandlerThrows_StaysConnected()
    {
        _handler.ThrowOnConnect = true;

        await StartAndWaitForStatusAsync(RelayStatus.Connected);
        await Eventually.TrueAsync(() => _handler.Sessions.Count == 1, "the handler was called");

        _supervisor.Status.ShouldBe(RelayStatus.Connected);
        _transport.IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task Stop_DuringSessionSetup_DoesNotThrowAndStillPublishesOffline()
    {
        // Regression: MQTTnet reports a cancelled publish/subscribe as a timeout exception, which escaped
        // from StopAsync when the app was stopped right after connecting.
        _handler.HangUntilCancelled = true;
        await StartAndWaitForStatusAsync(RelayStatus.Connected);
        await Eventually.TrueAsync(() => _handler.Sessions.Count == 1, "session setup is in progress");

        await Should.NotThrowAsync(() => _supervisor.StopAsync(TestContext.Current.CancellationToken));

        _transport.Calls.Last().ShouldBe("disconnect");
        _transport.PublishedTo(Availability).Last().Payload.ShouldBe("offline");
    }

    [Fact]
    public async Task UnexpectedErrorInLoop_IsRetriedInsteadOfEndingTheLoop()
    {
        // Regression: any exception other than a cancellation used to end the loop, so the app would
        // never reconnect again.
        var protector = new ThrowOnceProtector();
        var settings = FakeSettingsState.Configured();
        settings.Mqtt.PasswordProtected = new FakeSecretProtector().Protect("secret");
        _settings.Set(settings);
        await using var supervisor = new MqttConnectionSupervisor(
            _transport, _handler, _settings, protector, new ReconnectBackoff(new NoJitterRandom()), _time, NullLogger<MqttConnectionSupervisor>.Instance);

        await supervisor.StartAsync(TestContext.Current.CancellationToken);

        await Eventually.TrueAsync(
            () => supervisor.Status == RelayStatus.Connected,
            "it recovers after the backoff delay",
            betweenChecks: () => _time.Advance(TimeSpan.FromMilliseconds(500)));
        protector.Calls.ShouldBe(2);
        await supervisor.StopAsync(TestContext.Current.CancellationToken);
    }

    private async Task StartAndWaitForStatusAsync(RelayStatus status)
    {
        await _supervisor.StartAsync(TestContext.Current.CancellationToken);
        await Eventually.TrueAsync(() => _supervisor.Status == status, $"status is {status}");
    }

    private sealed class RecordingSessionHandler : IMqttSessionHandler
    {
        public ConcurrentQueue<(MqttTopics Topics, AppSettings Settings)> Sessions { get; } = new();

        public bool ThrowOnConnect { get; set; }

        /// <summary>Waits until cancelled, then throws a non-cancellation exception, as MQTTnet does.</summary>
        public bool HangUntilCancelled { get; set; }

        public async Task OnConnectedAsync(MqttTopics topics, AppSettings settings, CancellationToken cancellationToken)
        {
            Sessions.Enqueue((topics, settings));
            if (ThrowOnConnect)
            {
                throw new InvalidOperationException("Session setup failed.");
            }

            if (HangUntilCancelled)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("The operation has timed out.");
                }
            }
        }
    }

    private sealed class ThrowOnceProtector : SmartHomeRelay.Core.Abstractions.ISecretProtector
    {
        private readonly FakeSecretProtector _inner = new();

        public int Calls { get; private set; }

        public string Protect(string plainText) => _inner.Protect(plainText);

        public string Unprotect(string protectedValue) =>
            ++Calls == 1 ? throw new InvalidOperationException("Unexpected failure.") : _inner.Unprotect(protectedValue);
    }

    private sealed class NoJitterRandom : Random
    {
        public override double NextDouble() => 0.5;
    }
}
