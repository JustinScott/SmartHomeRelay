using Microsoft.Extensions.Time.Testing;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Features.Hardware;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Hardware;

public sealed class HardwareModuleTests : IAsyncDisposable
{
    private readonly FakeLhmEndpoint _endpoint = new();
    private readonly FakeSettingsState _settings;
    private readonly FakeSecretProtector _protector = new();
    private readonly FakeUserNotifier _notifier = new();
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingModuleContext _context = new();
    private HardwareModule _module;

    public HardwareModuleTests()
    {
        _settings = new FakeSettingsState(WithHardware(h => { }));
        _module = Create();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _module.StopAsync(CancellationToken.None);
        _module.Dispose();
    }

    [Fact]
    public void IsEnabled_OffByDefault()
    {
        // Spec 5.8: never competes with HA's own LHM integration unless turned on.
        _module.IsEnabled(new AppSettings()).ShouldBeFalse();
        _module.IsEnabled(WithHardware(h => { })).ShouldBeTrue();
    }

    [Fact]
    public void DescribeEntities_DefaultSensorsPlusDiagnostics()
    {
        _module.DescribeEntities().Select(e => e.Key).ShouldBe(["cpu_temperature", "cpu_fan", "ssd_temperature", "hardware_status", "lhm_version"]);
    }

    [Fact]
    public void DescribeEntities_SensorsFollowSettings_AndDependOnStatus()
    {
        _settings.Set(WithHardware(h => h.Sensors = ["cpu_fan"]));
        _module = Create();

        var entities = _module.DescribeEntities();
        entities.Select(e => e.Key).ShouldBe(["cpu_fan", "hardware_status", "lhm_version"]);
        entities[0].AvailableWhen!.StatusKey.ShouldBe("hardware_status");
        entities[0].AvailableWhen!.AvailableStates.ShouldBe(["ok", "newer_than_tested"]);
    }

    [Fact]
    public async Task Sample_Trusted_PublishesValuesStatusAndVersion()
    {
        await StartAsync();
        await _module.SampleAsync(Ct);

        _context.Latest("cpu_temperature").ShouldBe("47");
        _context.Latest("cpu_fan").ShouldBe("1250");
        _context.Latest("ssd_temperature").ShouldBe("38");
        _context.Latest("hardware_status").ShouldBe("ok");
        _context.Latest("lhm_version").ShouldBe("0.9.6");
        _notifier.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sample_SendsTheDecryptedLogin()
    {
        _settings.Set(WithHardware(h =>
        {
            h.Username = "relay";
            h.PasswordProtected = _protector.Protect("s3cret");
        }));
        _module = Create();
        await StartAsync();

        await _module.SampleAsync(Ct);

        _endpoint.Requests.Single().ShouldBe(new LhmCredentials("relay", "s3cret"));
    }

    [Fact]
    public async Task Sample_PasswordFromAnotherAccount_IsLoginFailed()
    {
        _settings.Set(WithHardware(h =>
        {
            h.Username = "relay";
            h.PasswordProtected = "not-dpapi-for-this-user";
        }));
        _module = Create();
        await StartAsync();

        await _module.SampleAsync(Ct);

        _context.Latest("hardware_status").ShouldBe("login_failed");
        _endpoint.Requests.ShouldBeEmpty();
        _notifier.Notifications.Single().Message.ShouldContain("enter it again");
    }

    [Fact]
    public async Task Sample_SmallChanges_FollowTheDeadband()
    {
        await StartAsync();
        await _module.SampleAsync(Ct);

        _endpoint.SetRawValue("/amdcpu/0/temperature/2", "48.1 °C"); // +0.8
        _endpoint.SetRawValue("/lpc/nct6798d/0/fan/1", "1600 RPM"); // +350
        await _module.SampleAsync(Ct);

        _context.Count("cpu_temperature").ShouldBe(1);
        _context.Latest("cpu_fan").ShouldBe("1600");
        _context.Count("hardware_status").ShouldBe(1, "unchanged status isn't republished");
    }

    [Fact]
    public async Task Sample_DataFormatChanged_IsIncompatible_PublishesNothingAndNamesTheWorkingVersion()
    {
        await StartAsync();
        _endpoint.Response = new(LhmHttpOutcome.Ok, LhmFixtures.Without(LhmFixtures.Box1, "/amdcpu/0/temperature/2", "RawValue"));

        await _module.SampleAsync(Ct);

        _context.Latest("hardware_status").ShouldBe("incompatible");
        _context.Latest("cpu_temperature").ShouldBeNull();
        var message = _notifier.Notifications.Single().Message;
        message.ShouldContain("LibreHardwareMonitor 0.9.6 isn't compatible");
        message.ShouldContain("Version 0.9.6 is known to work");
    }

    [Fact]
    public async Task Sample_UnitChanged_IsIncompatible()
    {
        // Same number, different meaning: e.g. LHM starts reporting Fahrenheit.
        await StartAsync();
        _endpoint.SetRawValue("/amdcpu/0/temperature/2", "117.1 °F");

        await _module.SampleAsync(Ct);

        _context.Latest("hardware_status").ShouldBe("incompatible");
        _context.Latest("cpu_temperature").ShouldBeNull();
    }

    [Fact]
    public async Task Sample_NewerThanTested_PublishesWithAOneTimeWarning()
    {
        _endpoint.Snapshot = FakeLhmEndpoint.Trusted(new Version(0, 9, 8, 0));
        await StartAsync();

        await _module.SampleAsync(Ct);
        await _module.SampleAsync(Ct);

        _context.Latest("hardware_status").ShouldBe("newer_than_tested");
        _context.Latest("cpu_temperature").ShouldBe("47");
        _context.Latest("lhm_version").ShouldBe("0.9.8");
        _notifier.Notifications.Single().Message.ShouldContain("newer than the tested version 0.9.6");
    }

    [Fact]
    public async Task Sample_Untrusted_IsNotReadAtAll()
    {
        _endpoint.Snapshot = new LhmEndpointSnapshot(PortOwnerKind.Process, @"C:\Temp\fake.exe", false, null, FakeLhmEndpoint.Trusted(FakeLhmEndpoint.Tested).Processes);
        await StartAsync();

        await _module.SampleAsync(Ct);

        _endpoint.Requests.ShouldBeEmpty("never send the login to an untrusted listener");
        _context.Latest("hardware_status").ShouldBe("untrusted");
        _notifier.Notifications.Single().Message.ShouldContain("fake.exe");
    }

    [Theory]
    [InlineData(LhmHttpOutcome.LoginFailed, "login_failed")]
    [InlineData(LhmHttpOutcome.LoginRequired, "login_failed")]
    [InlineData(LhmHttpOutcome.LoginNotRequested, "untrusted")]
    [InlineData(LhmHttpOutcome.TooLarge, "incompatible")]
    [InlineData(LhmHttpOutcome.UnexpectedResponse, "incompatible")]
    [InlineData(LhmHttpOutcome.NotListening, "not_running")]
    [InlineData(LhmHttpOutcome.Timeout, "not_running")]
    public async Task Sample_HttpProblems_MapToStatus(LhmHttpOutcome outcome, string status)
    {
        await StartAsync();
        _endpoint.Response = new(outcome);

        await _module.SampleAsync(Ct);

        _context.Latest("hardware_status").ShouldBe(status);
    }

    [Fact]
    public async Task NotRunning_AtStartup_IsQuietDuringTheGracePeriod_ThenNotifiedOnce()
    {
        _endpoint.Snapshot = new LhmEndpointSnapshot(PortOwnerKind.None, null, false, null, []);
        await StartAsync();

        await _module.SampleAsync(Ct);
        _context.Latest("hardware_status").ShouldBe("not_running");
        _notifier.Notifications.ShouldBeEmpty("LHM may still be starting at sign-in");

        _time.Advance(HardwareModule.NotRunningGrace);
        await _module.SampleAsync(Ct);
        await _module.SampleAsync(Ct);

        _notifier.Notifications.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Recovery_RepublishesValues_AndALaterProblemIsNewsAgain()
    {
        await StartAsync();
        await _module.SampleAsync(Ct);
        _endpoint.Response = new(LhmHttpOutcome.LoginFailed);
        await _module.SampleAsync(Ct);

        _endpoint.Response = new(LhmHttpOutcome.Ok, LhmFixtures.Box1);
        await _module.SampleAsync(Ct);
        _context.Count("cpu_temperature").ShouldBe(2, "values are republished after a problem, even if unchanged");
        _context.Latest("hardware_status").ShouldBe("ok");

        _endpoint.Response = new(LhmHttpOutcome.LoginFailed);
        await _module.SampleAsync(Ct);
        _notifier.Notifications.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Sample_UnmatchedSensor_IsUnknown_OthersStillPublished()
    {
        _endpoint.SystemDisk = null;
        await StartAsync();

        await _module.SampleAsync(Ct);
        await _module.SampleAsync(Ct);

        _context.Latest("ssd_temperature").ShouldBe(HardwareModule.UnknownPayload);
        _context.Count("ssd_temperature").ShouldBe(1);
        _context.Latest("cpu_temperature").ShouldBe("47");
    }

    [Fact]
    public async Task Sample_ImplausibleValue_IsDropped()
    {
        await StartAsync();
        await _module.SampleAsync(Ct);
        _endpoint.SetRawValue("/amdcpu/0/temperature/2", "255.0 °C");

        await _module.SampleAsync(Ct);

        _context.Latest("cpu_temperature").ShouldBe("47");
    }

    [Fact]
    public async Task Loop_SamplesAtTheConfiguredInterval_AndStopsWhileAsleep()
    {
        _settings.Set(WithHardware(h => h.IntervalSeconds = 5));
        await _module.StartAsync(_context, Ct);
        await Eventually.TrueAsync(() => _endpoint.Requests.Count == 1, "first sample after the initial delay", () => _time.Advance(TimeSpan.FromMilliseconds(500)));

        await Eventually.TrueAsync(() => _endpoint.Requests.Count == 2, "second sample after the interval", () => _time.Advance(TimeSpan.FromSeconds(5)));

        await _module.OnSuspendingAsync(Ct);
        var count = _endpoint.Requests.Count;
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Yield();
        _endpoint.Requests.Count.ShouldBe(count);
    }

    private static AppSettings WithHardware(Action<HardwareSettings> configure)
    {
        var settings = FakeSettingsState.Configured();
        settings.Features.Hardware.Enabled = true;
        configure(settings.Features.Hardware);
        return settings;
    }

    private HardwareModule Create() => new(_endpoint, _settings, _protector, _notifier, _time);

    /// <summary>Starts without the loop, so tests drive each sample themselves.</summary>
    private Task StartAsync()
    {
        _module.Attach(_context);
        return Task.CompletedTask;
    }
}
