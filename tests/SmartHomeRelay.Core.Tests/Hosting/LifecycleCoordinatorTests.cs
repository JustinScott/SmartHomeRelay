using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Hosting;

/// <summary>
/// Suspend, resume, shutdown, and network handling (spec 6), driven by fake OS events against the real
/// supervisor and module host over a fake transport and a fake clock.
/// </summary>
public sealed class LifecycleCoordinatorTests : IAsyncDisposable
{
    private const string Availability = "shr/DESKTOP-ABC123/status";
    private const string UptimeConfig = "homeassistant/sensor/DESKTOP-ABC123/uptime/config";
    private const string UptimeState = "shr/DESKTOP-ABC123/uptime/state";

    private readonly FakeMqttTransport _transport = new();
    private readonly FakeSettingsState _settings = new(FakeSettingsState.Configured());
    private readonly FakeTimeProvider _time = new();
    private readonly FakeSystemEventSource _events = new();
    private readonly FakeApplicationLifetime _lifetime = new();
    private readonly ConcurrentQueue<string> _journal = new();
    private readonly FakePowerAwareModule _module;
    private readonly FakePowerAwareModule _otherModule;
    private readonly ModuleHost _host;
    private readonly MqttConnectionSupervisor _supervisor;
    private readonly LifecycleCoordinator _coordinator;

    public LifecycleCoordinatorTests()
    {
        _module = new FakePowerAwareModule("power", _journal, FakeModule.Sensor("uptime"));
        _otherModule = new FakePowerAwareModule("metrics", _journal, FakeModule.Sensor("cpu_usage"));
        _host = new ModuleHost([_module, _otherModule], _transport, _settings, NullLoggerFactory.Instance);
        _supervisor = new MqttConnectionSupervisor(
            _transport, _host, _settings, new FakeSecretProtector(), new ReconnectBackoff(new NoJitterRandom()), _time,
            NullLogger<MqttConnectionSupervisor>.Instance);
        _coordinator = new LifecycleCoordinator(_events, _supervisor, _host, _lifetime, _time, NullLogger<LifecycleCoordinator>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _coordinator.StopAsync(CancellationToken.None);

        // The supervisor's offline step uses the fake clock; keep it moving so cleanup can never hang.
        var stopped = _supervisor.DisposeAsync().AsTask();
        await Eventually.TrueAsync(() => stopped.IsCompleted, "the supervisor stops", betweenChecks: () => _time.Advance(TimeSpan.FromMilliseconds(250)));
        await stopped;
        _host.Dispose();
    }

    [Fact]
    public async Task Suspend_WhileConnected_PublishesOfflineBeforeDisconnecting()
    {
        // Spec 9 regression: the prototype disconnected without publishing offline, so HA showed the PC
        // as online while it slept.
        await StartConnectedAsync();

        await _events.RaiseSuspending();

        var calls = _transport.Calls.ToList();
        calls[^1].ShouldBe("disconnect");
        calls[^2].ShouldBe($"publish {Availability} offline");
        var offline = _transport.PublishedTo(Availability).Last();
        offline.Retain.ShouldBeTrue();
        offline.Qos.ShouldBe(MqttQos.AtLeastOnce);
        _supervisor.Status.ShouldBe(RelayStatus.Suspended);
    }

    [Fact]
    public async Task Suspend_StaysOfflineUntilResume()
    {
        await StartConnectedAsync();
        await _events.RaiseSuspending();

        // Ten minutes pass (as far as timers can tell): no reconnect attempts while asleep.
        for (var i = 0; i < 20; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(30));
            await Task.Yield();
        }

        _transport.ConnectAttempts.Count.ShouldBe(1);
        _supervisor.Status.ShouldBe(RelayStatus.Suspended);
    }

    [Fact]
    public async Task Resume_ReconnectsImmediatelyAndRepublishesOnlineDiscoveryAndState()
    {
        await StartConnectedAsync();
        await _module.Context!.PublishStateAsync("uptime", "3600", Ct);
        await _events.RaiseSuspending();

        _events.RaiseResumed();

        // No clock advance: resume must not wait out a backoff delay.
        await Eventually.TrueAsync(() => _supervisor.Status == RelayStatus.Connected, "it reconnects after resume");
        // Once when the module published it, once more after the reconnect.
        await Eventually.TrueAsync(() => _transport.PublishedTo(UptimeState).Count == 2, "full state is republished");
        _transport.ConnectAttempts.Count.ShouldBe(2);
        _transport.PublishedTo(Availability).Select(p => p.Payload).ShouldBe(["online", "offline", "online"]);
        _transport.PublishedTo(UptimeConfig).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Suspend_PausesPowerAwareModulesAfterGoingOffline()
    {
        await StartConnectedAsync();
        var offlineAlreadyPublished = false;
        _module.OnSuspend = () => offlineAlreadyPublished = _transport.PublishedTo(Availability).Last().Payload == "offline";

        await _events.RaiseSuspending();

        _journal.ShouldContain("power suspending");
        _journal.ShouldContain("metrics suspending");
        offlineAlreadyPublished.ShouldBeTrue();
    }

    [Fact]
    public async Task Resume_ResumesModulesBeforeReconnecting()
    {
        await StartConnectedAsync();
        await _events.RaiseSuspending();
        var connectsBeforeResume = _transport.ConnectAttempts.Count;
        var connectsWhenModuleResumed = -1;
        _module.OnResume = () => connectsWhenModuleResumed = _transport.ConnectAttempts.Count;
        _journal.Clear();

        _events.RaiseResumed();

        await Eventually.TrueAsync(() => _supervisor.Status == RelayStatus.Connected, "it reconnects");
        _journal.ShouldContain("power resumed");
        _journal.ShouldContain("metrics resumed");
        connectsWhenModuleResumed.ShouldBe(connectsBeforeResume, "modules resume before the new connection");
        _transport.ConnectAttempts.Count.ShouldBe(connectsBeforeResume + 1);
    }

    [Fact]
    public async Task Suspend_ModuleThrows_OtherModulesStillPausedAndNoCrash()
    {
        await StartConnectedAsync();
        _module.ThrowOnSuspend = new InvalidOperationException("boom");

        await Should.NotThrowAsync(_events.RaiseSuspending());

        _journal.ShouldContain("metrics suspending");
        _transport.PublishedTo(Availability).Last().Payload.ShouldBe("offline");
    }

    [Fact]
    public async Task Suspend_NetworkStalled_FinishesWithinBudget()
    {
        // Windows only waits ~2s. If publishing hangs, give up in time; the last will covers it.
        await StartConnectedAsync();
        _transport.HangPublishes = true;

        var suspended = _events.RaiseSuspending();

        await Eventually.TrueAsync(
            () => suspended.IsCompleted,
            "the deferred work gives up when the budget runs out",
            betweenChecks: () => _time.Advance(TimeSpan.FromMilliseconds(250)));

        // Regression: the stalled connection used to stay open, keeping the PC "online" at the broker.
        await Eventually.TrueAsync(
            () => _transport.Calls.Contains("abort"),
            "the connection is dropped so the last will fires",
            betweenChecks: () => _time.Advance(TimeSpan.FromMilliseconds(250)));
        _transport.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task Suspend_DuringConnectAttempt_AbortsItAndNeverAnnouncesOnline()
    {
        _transport.HangConnects = true;
        await StartAsync();
        await Eventually.TrueAsync(() => _transport.ConnectAttempts.Count == 1, "a connection attempt is in progress");

        await _events.RaiseSuspending();

        _supervisor.Status.ShouldBe(RelayStatus.Suspended);
        _transport.PublishedTo(Availability).ShouldNotContain(p => p.Payload == "online");
    }

    [Fact]
    public async Task Resume_WithoutSuspend_ForcesFreshConnection()
    {
        // Modern Standby can skip the suspend event; the old connection is then probably dead.
        await StartConnectedAsync();

        _events.RaiseResumed();

        await Eventually.TrueAsync(() => _transport.ConnectAttempts.Count == 2, "it reconnects");
        await Eventually.TrueAsync(() => _supervisor.Status == RelayStatus.Connected, "it is connected again");
        _transport.PublishedTo(Availability).Select(p => p.Payload).ShouldBe(["online", "offline", "online"]);
    }

    [Fact]
    public async Task NetworkAvailable_WhileWaitingToRetry_RetriesImmediately()
    {
        _transport.FailNextConnects(1);
        await StartAsync();
        await Eventually.TrueAsync(() => _supervisor.Status == RelayStatus.Reconnecting, "the first attempt failed");

        _events.RaiseNetworkAvailabilityChanged(true);

        // No clock advance: the retry must not wait for the backoff delay.
        await Eventually.TrueAsync(() => _supervisor.Status == RelayStatus.Connected, "it retries at once");
        _transport.ConnectAttempts.Count.ShouldBe(2);
    }

    [Fact]
    public async Task NetworkAvailable_WhileConnected_DoesNotReconnect()
    {
        await StartConnectedAsync();

        _events.RaiseNetworkAvailabilityChanged(true);

        _supervisor.HasPendingWake.ShouldBeFalse();
        _transport.ConnectAttempts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task NetworkAvailable_WhileSuspended_StaysOffline()
    {
        await StartConnectedAsync();
        await _events.RaiseSuspending();

        _events.RaiseNetworkAvailabilityChanged(true);

        _supervisor.HasPendingWake.ShouldBeFalse();
        _supervisor.Status.ShouldBe(RelayStatus.Suspended);
    }

    [Fact]
    public async Task SessionEnding_PublishesOfflineThenStopsTheApp()
    {
        await StartConnectedAsync();

        await _events.RaiseSessionEnding();

        var calls = _transport.Calls.ToList();
        calls[^1].ShouldBe("disconnect");
        calls[^2].ShouldBe($"publish {Availability} offline");
        _lifetime.StopRequests.ShouldBe(1);
    }

    [Fact]
    public async Task Suspend_NotConfigured_CompletesAndResumeReturnsToNotConfigured()
    {
        _settings.Set(TestSettings.Valid());
        await StartAsync();
        await Eventually.TrueAsync(() => _supervisor.Status == RelayStatus.NotConfigured, "not configured");

        await _events.RaiseSuspending();
        _supervisor.Status.ShouldBe(RelayStatus.Suspended);

        _events.RaiseResumed();
        await Eventually.TrueAsync(() => _supervisor.Status == RelayStatus.NotConfigured, "back to not configured");
        _transport.ConnectAttempts.ShouldBeEmpty();
    }

    [Fact]
    public async Task SessionSwitched_IsHandledWithoutAffectingTheConnection()
    {
        await StartConnectedAsync();

        _events.RaiseSessionSwitched(SessionSwitchKind.Lock);
        _events.RaiseSessionSwitched(SessionSwitchKind.Unlock);

        _supervisor.Status.ShouldBe(RelayStatus.Connected);
        _transport.ConnectAttempts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Stop_UnsubscribesFromSystemEvents()
    {
        await StartAsync();

        await _coordinator.StopAsync(Ct);

        _events.HasSubscribers.ShouldBeFalse();
    }

    private async Task StartAsync()
    {
        await _host.StartAsync(Ct);
        await _supervisor.StartAsync(Ct);
        await _coordinator.StartAsync(Ct);
    }

    private async Task StartConnectedAsync()
    {
        await StartAsync();
        await Eventually.TrueAsync(() => _supervisor.Status == RelayStatus.Connected, "connected");
        await Eventually.TrueAsync(() => _transport.PublishedTo(UptimeConfig).Count == 1, "session set up");
    }

    private sealed class NoJitterRandom : Random
    {
        public override double NextDouble() => 0.5;
    }
}
