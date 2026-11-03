using Microsoft.Extensions.Time.Testing;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Features.Power;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Power;

public sealed class PowerModuleTests : IAsyncDisposable
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 2, 18, 30, 0, TimeSpan.Zero);

    private readonly FakePowerController _power = new();
    private readonly FakeSettingsState _settings = new(FakeSettingsState.Configured());
    private readonly FakeTimeProvider _time = new(s_now);
    private readonly RecordingModuleContext _context = new();
    private PowerModule _module;

    public PowerModuleTests()
    {
        _module = new PowerModule(_power, _power, _settings, _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _module.StopAsync(CancellationToken.None);
        _module.Dispose();
    }

    [Fact]
    public void DescribeEntities_HibernationEnabled_OffersAllButtonsAndSensors()
    {
        _module.DescribeEntities().Select(e => e.Key).ShouldBe(
            ["shutdown", "restart", "cancel_shutdown", "sleep", "hibernate", "lock", "sign_out", "last_boot", "uptime"]);
    }

    [Fact]
    public void DescribeEntities_HibernationDisabled_DoesNotOfferHibernate()
    {
        _power.IsHibernateAvailable = false;
        _module = new PowerModule(_power, _power, _settings, _time);

        _module.DescribeEntities().Select(e => e.Key).ShouldNotContain("hibernate");
    }

    [Fact]
    public void DescribeEntities_AreStableAcrossCalls()
    {
        var first = _module.DescribeEntities();
        _power.IsHibernateAvailable = false; // changing later must not change the keys (module contract)

        _module.DescribeEntities().ShouldBeSameAs(first);
    }

    [Theory]
    [InlineData("sleep", "sleep")]
    [InlineData("lock", "lock")]
    [InlineData("cancel_shutdown", "cancel")]
    public async Task Press_AlwaysAllowedAction_Runs(string key, string expectedAction)
    {
        await PressAsync(key);

        _power.Actions.ShouldBe([expectedAction]);
    }

    [Theory]
    [InlineData("shutdown")]
    [InlineData("restart")]
    [InlineData("hibernate")]
    [InlineData("sign_out")]
    public async Task Press_DestructiveActionByDefault_IsIgnored(string key)
    {
        // Spec 5.2: destructive commands are disabled by default. The button exists but does nothing.
        await PressAsync(key);

        _power.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Press_ShutdownWhenAllowed_UsesDefaultSixtySecondDelay()
    {
        Allow(p => p.AllowShutdown = true);

        await PressAsync("shutdown");

        _power.Actions.ShouldBe(["shutdown 60s"]);
    }

    [Fact]
    public async Task Press_RestartWhenAllowed_UsesConfiguredDelay()
    {
        Allow(p =>
        {
            p.AllowRestart = true;
            p.ShutdownDelaySeconds = 0;
        });

        await PressAsync("restart");

        _power.Actions.ShouldBe(["restart 0s"]);
    }

    [Theory]
    [InlineData("hibernate", "hibernate")]
    [InlineData("sign_out", "sign_out")]
    public async Task Press_OtherDestructiveActionsWhenAllowed_Run(string key, string expectedAction)
    {
        Allow(p =>
        {
            p.AllowHibernate = true;
            p.AllowSignOut = true;
        });

        await PressAsync(key);

        _power.Actions.ShouldBe([expectedAction]);
    }

    [Fact]
    public async Task Press_AllowFlagReadAtPressTime_SoTurningItOffTakesEffectImmediately()
    {
        Allow(p => p.AllowShutdown = true);
        _settings.Set(FakeSettingsState.Configured()); // allowShutdown back to false

        await PressAsync("shutdown");

        _power.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Press_InvalidSettings_RefusesDestructiveActions()
    {
        // A broken settings file must never allow a shutdown.
        var settings = FakeSettingsState.Configured();
        settings.Features.Power.AllowShutdown = true;
        _settings.Set(settings, ["mqtt.port must be between 1 and 65535."]);

        await PressAsync("shutdown");

        _power.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Press_PayloadOtherThanPress_IsIgnored()
    {
        await PressAsync("sleep", payload: "ON");

        _power.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Press_ControllerFails_PropagatesSoTheHostLogsIt()
    {
        _power.ThrowOnNextAction = new InvalidOperationException("shutdown.exe failed");

        await Should.ThrowAsync<InvalidOperationException>(() => PressAsync("sleep"));
    }

    [Fact]
    public async Task Start_PublishesLastBootAsUtcTimestampAndUptime()
    {
        _power.Uptime = TimeSpan.FromHours(3) + TimeSpan.FromSeconds(42);

        await _module.StartAsync(_context, Ct);

        _context.Latest(PowerKeys.LastBoot).ShouldBe("2026-11-02T15:29:18+00:00");
        _context.Latest(PowerKeys.Uptime).ShouldBe("3.0"); // hours, rounded down to a tenth
    }

    [Fact]
    public async Task Uptime_PublishedEveryFiveMinutesOnly()
    {
        await _module.StartAsync(_context, Ct);

        _power.Uptime += TimeSpan.FromMinutes(4);
        _time.Advance(TimeSpan.FromMinutes(4));
        await Task.Yield();
        _context.Count(PowerKeys.Uptime).ShouldBe(1);

        _power.Uptime += TimeSpan.FromMinutes(1);
        await Eventually.TrueAsync(() => _context.Count(PowerKeys.Uptime) == 2, "uptime is published at 5 minutes", () => _time.Advance(TimeSpan.FromMinutes(1)));
        _context.Latest(PowerKeys.Uptime).ShouldBe("3.0"); // 3 h 5 min is still under 3.1 h
    }

    [Fact]
    public async Task Suspend_StopsUptimeAndResumePublishesImmediately()
    {
        await _module.StartAsync(_context, Ct);
        await _module.OnSuspendingAsync(Ct);

        for (var i = 0; i < 3; i++)
        {
            _time.Advance(UptimeIntervalPlus());
            await Task.Yield();
        }

        _context.Count(PowerKeys.Uptime).ShouldBe(1);

        _power.Uptime = TimeSpan.FromHours(10);
        await _module.OnResumedAsync(Ct);
        _context.Latest(PowerKeys.Uptime).ShouldBe("10.0");
    }

    [Theory]
    [InlineData(0, "0.0")]
    [InlineData(359, "0.0")] // 5 min 59 s
    [InlineData(360, "0.1")] // 6 min
    [InlineData(49_000, "13.6")] // 13 h 36 min 40 s
    [InlineData(1_000_000, "277.7")] // over 11 days: still hours, never "1E+..." or a thousands separator
    [InlineData(-5, "0.0")]
    public void FormatUptime_IsHoursRoundedDownToATenth(double seconds, string expected)
    {
        PowerModule.FormatUptime(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
    }

    [Fact]
    public void IsEnabled_FollowsPowerToggle()
    {
        var settings = FakeSettingsState.Configured();
        _module.IsEnabled(settings).ShouldBeTrue();

        settings.Features.Power.Enabled = false;
        _module.IsEnabled(settings).ShouldBeFalse();
    }

    private static TimeSpan UptimeIntervalPlus() => PowerModule.UptimeInterval + TimeSpan.FromSeconds(1);

    private void Allow(Action<PowerSettings> change)
    {
        var settings = FakeSettingsState.Configured();
        change(settings.Features.Power);
        _settings.Set(settings);
    }

    private Task PressAsync(string key, string payload = "PRESS") =>
        _module.DescribeEntities().Single(e => e.Key == key).CommandHandler!(payload, Ct);
}
