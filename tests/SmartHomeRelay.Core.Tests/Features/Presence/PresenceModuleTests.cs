using Microsoft.Extensions.Time.Testing;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Features.Presence;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Presence;

public sealed class PresenceModuleTests : IAsyncDisposable
{
    private readonly FakePresenceSources _sources = new();
    private readonly FakeSystemEventSource _events = new();
    private readonly FakeSettingsState _settings = new(FakeSettingsState.Configured());
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingModuleContext _context = new();
    private readonly PresenceModule _module;

    public PresenceModuleTests()
    {
        _module = new PresenceModule(_events, _sources, _sources, _settings, _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _module.StopAsync(CancellationToken.None);
        _module.Dispose();
    }

    [Fact]
    public async Task Start_PublishesAllFiveValues()
    {
        _sources.Idle = TimeSpan.FromSeconds(2);

        await _module.StartAsync(_context, Ct);

        _context.Latest(PresenceKeys.SessionLocked).ShouldBe("OFF");
        _context.Latest(PresenceKeys.LoggedInUser).ShouldBe("Justin");
        _context.Latest(PresenceKeys.IdleSeconds).ShouldBe("0");
        _context.Latest(PresenceKeys.UserActive).ShouldBe("ON");
        _context.Latest(PresenceKeys.PresenceState).ShouldBe("active");
        _context.Published.Count.ShouldBe(5);
    }

    [Fact]
    public async Task Start_WhileLocked_PublishesLocked()
    {
        _sources.Locked = true;

        await _module.StartAsync(_context, Ct);

        _context.Latest(PresenceKeys.SessionLocked).ShouldBe("ON");
        _context.Latest(PresenceKeys.PresenceState).ShouldBe("locked");
        _context.Latest(PresenceKeys.UserActive).ShouldBe("OFF");
    }

    [Fact]
    public async Task LockEvent_PublishesImmediatelyWithoutWaitingForASample()
    {
        await _module.StartAsync(_context, Ct);

        _events.RaiseSessionSwitched(SessionSwitchKind.Lock);

        // No clock advance: lock/unlock must not wait for the 5-second sample.
        await Eventually.TrueAsync(() => _context.Latest(PresenceKeys.PresenceState) == "locked", "locked is published");
        _context.Latest(PresenceKeys.SessionLocked).ShouldBe("ON");
        _context.Latest(PresenceKeys.UserActive).ShouldBe("OFF");
    }

    [Fact]
    public async Task UnlockEvent_ReturnsToActive()
    {
        _sources.Locked = true;
        await _module.StartAsync(_context, Ct);

        _events.RaiseSessionSwitched(SessionSwitchKind.Unlock);

        await Eventually.TrueAsync(() => _context.Latest(PresenceKeys.PresenceState) == "active", "active is published");
        _context.Latest(PresenceKeys.SessionLocked).ShouldBe("OFF");
        _context.Latest(PresenceKeys.UserActive).ShouldBe("ON");
    }

    [Fact]
    public async Task LogonEvent_CountsAsUnlocked()
    {
        _sources.Locked = true;
        await _module.StartAsync(_context, Ct);

        _events.RaiseSessionSwitched(SessionSwitchKind.Logon);

        await Eventually.TrueAsync(() => _context.Latest(PresenceKeys.SessionLocked) == "OFF", "unlocked is published");
    }

    [Theory]
    [InlineData(SessionSwitchKind.RemoteConnect)]
    [InlineData(SessionSwitchKind.ConsoleDisconnect)]
    [InlineData(SessionSwitchKind.Other)]
    public async Task OtherSessionEvents_DoNotChangeLockState(SessionSwitchKind kind)
    {
        await _module.StartAsync(_context, Ct);

        _events.RaiseSessionSwitched(kind);

        _context.Published.Count.ShouldBe(5); // only the initial publish
    }

    [Fact]
    public async Task Sample_IdleCrossesThreshold_PublishesIdleAndOnlyWhatChanged()
    {
        await _module.StartAsync(_context, Ct);
        _sources.Idle = TimeSpan.FromMinutes(5);

        await AdvanceOneSampleAsync(() => _context.Latest(PresenceKeys.PresenceState) == "idle");

        _context.Latest(PresenceKeys.UserActive).ShouldBe("OFF");
        _context.Latest(PresenceKeys.IdleSeconds).ShouldBe("300");
        _context.Count(PresenceKeys.SessionLocked).ShouldBe(1, "unchanged values are not republished");
        _context.Count(PresenceKeys.LoggedInUser).ShouldBe(1);
    }

    [Fact]
    public async Task Sample_ActiveUser_PublishesNothing()
    {
        await _module.StartAsync(_context, Ct);

        for (var i = 0; i < 10; i++)
        {
            _sources.Idle = TimeSpan.FromSeconds(i % 4); // keeps typing: always under 5s
            _time.Advance(PresenceModule.SampleInterval);
            await Task.Yield();
        }

        // Prove the loop is running: a change now is picked up.
        _sources.Idle = TimeSpan.FromSeconds(30);
        await AdvanceOneSampleAsync(() => _context.Latest(PresenceKeys.IdleSeconds) == "30");
        _context.Count(PresenceKeys.IdleSeconds).ShouldBe(2, "only the initial 0 and the 30");
    }

    [Fact]
    public async Task ThresholdChange_AppliesOnNextSample()
    {
        _sources.Idle = TimeSpan.FromSeconds(90);
        await _module.StartAsync(_context, Ct);
        _context.Latest(PresenceKeys.PresenceState).ShouldBe("active");

        var settings = FakeSettingsState.Configured();
        settings.Features.Presence.IdleThresholdSeconds = 60;
        _settings.Set(settings);

        await AdvanceOneSampleAsync(() => _context.Latest(PresenceKeys.PresenceState) == "idle");
    }

    [Fact]
    public async Task Suspend_StopsSampling()
    {
        await _module.StartAsync(_context, Ct);

        await _module.OnSuspendingAsync(Ct);
        var countAtSuspend = _context.Published.Count;
        _sources.Idle = TimeSpan.FromHours(1);
        for (var i = 0; i < 5; i++)
        {
            _time.Advance(PresenceModule.SampleInterval);
            await Task.Yield();
        }

        _context.Published.Count.ShouldBe(countAtSuspend);
    }

    [Fact]
    public async Task Resume_RereadsLockStateAndRestartsSampling()
    {
        // Windows can lock on sleep ("require sign-in on wake") without a lock event reaching us.
        await _module.StartAsync(_context, Ct);
        await _module.OnSuspendingAsync(Ct);
        _sources.Locked = true;

        await _module.OnResumedAsync(Ct);

        _context.Latest(PresenceKeys.SessionLocked).ShouldBe("ON");
        _context.Latest(PresenceKeys.PresenceState).ShouldBe("locked");

        _sources.Idle = TimeSpan.FromSeconds(20);
        await AdvanceOneSampleAsync(() => _context.Latest(PresenceKeys.IdleSeconds) == "20");
    }

    [Fact]
    public async Task Sample_IdleReadFails_KeepsSampling()
    {
        await _module.StartAsync(_context, Ct);
        _sources.ThrowOnNextIdleRead = new InvalidOperationException("boom");
        _time.Advance(PresenceModule.SampleInterval);
        await Task.Yield();

        _sources.Idle = TimeSpan.FromSeconds(15);
        await AdvanceOneSampleAsync(() => _context.Latest(PresenceKeys.IdleSeconds) == "15");
    }

    [Fact]
    public async Task Stop_UnsubscribesFromSessionEvents()
    {
        await _module.StartAsync(_context, Ct);

        await _module.StopAsync(Ct);

        _events.HasSubscribers.ShouldBeFalse();
    }

    [Fact]
    public async Task Restart_PublishesEverythingAgain()
    {
        // Disabling and re-enabling the feature: the host forgot the states, so all must be sent again.
        await _module.StartAsync(_context, Ct);
        await _module.StopAsync(Ct);
        var second = new RecordingModuleContext();

        await _module.StartAsync(second, Ct);

        second.Published.Count.ShouldBe(5);
    }

    [Fact]
    public void DescribeEntities_UsesSpecKeys()
    {
        _module.DescribeEntities().Select(e => e.Key).ShouldBe(
            ["session_locked", "logged_in_user", "idle_seconds", "user_active", "presence_state"]);
    }

    [Fact]
    public void IsEnabled_FollowsPresenceToggle()
    {
        var settings = FakeSettingsState.Configured();
        _module.IsEnabled(settings).ShouldBeTrue();

        settings.Features.Presence.Enabled = false;
        _module.IsEnabled(settings).ShouldBeFalse();
    }

    /// <summary>Advances the fake clock sample by sample until the condition holds.</summary>
    private Task AdvanceOneSampleAsync(Func<bool> condition) =>
        Eventually.TrueAsync(condition, "the next sample publishes the change", betweenChecks: () => _time.Advance(PresenceModule.SampleInterval));
}
