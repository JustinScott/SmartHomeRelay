using Microsoft.Extensions.Logging.Abstractions;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Configuration;

/// <summary>Spec 7.1: the settings window's logic, without the window.</summary>
public sealed class SettingsWindowLogicTests
{
    private readonly FakeSettingsState _state;
    private readonly RecordingStore _store = new();
    private readonly FakeSecretProtector _protector = new();
    private readonly SettingsEditor _editor;

    public SettingsWindowLogicTests()
    {
        var settings = FakeSettingsState.Configured();
        settings.Mqtt.PasswordProtected = _protector.Protect("old-secret");
        _state = new FakeSettingsState(settings);
        _editor = new SettingsEditor(_state, _store, _protector, new SettingsValidator());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Load_IsACopy_SoEditingDoesNotTouchTheRunningSettings()
    {
        var draft = _editor.Load();
        draft.Settings.Mqtt.Host = "changed";

        _state.Current.Settings.Mqtt.Host.ShouldBe("broker.local");
        draft.HasMqttPassword.ShouldBeTrue();
        draft.HasHardwarePassword.ShouldBeFalse();
        draft.FileProblems.ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_EmptyPasswordBox_KeepsTheSavedPassword()
    {
        var draft = _editor.Load();
        draft.Settings.Mqtt.Port = 1884;

        (await _editor.SaveAsync(draft.Settings, newMqttPassword: "", newHardwarePassword: null, Ct)).ShouldBeEmpty();

        var saved = _store.Saved.ShouldNotBeNull();
        saved.Mqtt.Port.ShouldBe(1884);
        _protector.Unprotect(saved.Mqtt.PasswordProtected).ShouldBe("old-secret");
    }

    [Fact]
    public async Task Save_NewPasswords_AreEncrypted_NeverPlain()
    {
        var draft = _editor.Load();

        await _editor.SaveAsync(draft.Settings, "new-secret", "lhm-secret", Ct);

        var saved = _store.Saved.ShouldNotBeNull();
        _protector.Unprotect(saved.Mqtt.PasswordProtected).ShouldBe("new-secret");
        _protector.Unprotect(saved.Features.Hardware.PasswordProtected).ShouldBe("lhm-secret");
        saved.Mqtt.Password.ShouldBeEmpty();
        saved.Features.Hardware.Password.ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_Invalid_ReturnsTheProblems_AndWritesNothing()
    {
        var draft = _editor.Load();
        draft.Settings.Device.Id = "has space";
        draft.Settings.Features.MediaPlayer.DuckLevelPercent = 150;

        var problems = await _editor.SaveAsync(draft.Settings, null, null, Ct);

        problems.Count.ShouldBe(2);
        _store.Saved.ShouldBeNull();
    }

    [Fact]
    public async Task Save_TrimsTypedText()
    {
        var draft = _editor.Load();
        draft.Settings.Mqtt.Host = "  broker.local  ";
        draft.Settings.Device.Name = " Desk PC ";

        await _editor.SaveAsync(draft.Settings, null, null, Ct);

        _store.Saved!.Mqtt.Host.ShouldBe("broker.local");
        _store.Saved.Device.Name.ShouldBe("Desk PC");
    }

    [Fact]
    public void Load_BrokenFile_ReportsItsProblems()
    {
        _state.Set(new AppSettings(), ["settings.json: not valid JSON."]);

        _editor.Load().FileProblems.ShouldBe(["settings.json: not valid JSON."]);
    }

    [Fact]
    public async Task TestConnection_Succeeds_WithTheSavedPassword_AndItsOwnClientId()
    {
        var transport = new FakeMqttTransport();
        var tester = new MqttConnectionTester(() => transport, _protector);

        var result = await tester.TestAsync(_state.Current.Settings.Mqtt, _state.Current.Settings.Device, null, Ct);

        result.Succeeded.ShouldBeTrue();
        var options = transport.ConnectAttempts.ShouldHaveSingleItem();
        options.ClientId.ShouldBe("shr-DESKTOP-ABC123-test", "the real connection uses shr-DESKTOP-ABC123");
        options.Password.ShouldBe("old-secret");
        transport.DisconnectCount.ShouldBe(1);
    }

    [Fact]
    public async Task TestConnection_TypedPassword_Wins()
    {
        var transport = new FakeMqttTransport();

        await new MqttConnectionTester(() => transport, _protector).TestAsync(_state.Current.Settings.Mqtt, _state.Current.Settings.Device, "typed", Ct);

        transport.ConnectAttempts.Single().Password.ShouldBe("typed");
    }

    [Fact]
    public async Task TestConnection_Refused_SaysSo_WithoutThePassword()
    {
        var transport = new FakeMqttTransport();
        transport.FailNextConnects(1, new MqttConnectionRefusedException("bad user name or password"));

        var result = await new MqttConnectionTester(() => transport, _protector).TestAsync(_state.Current.Settings.Mqtt, _state.Current.Settings.Device, "typed", Ct);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldContain("refused");
        result.Message.ShouldNotContain("typed");
    }

    [Fact]
    public async Task TestConnection_Unreachable_And_NoHost()
    {
        var transport = new FakeMqttTransport();
        transport.FailNextConnects(1);
        var tester = new MqttConnectionTester(() => transport, _protector);
        var mqtt = _state.Current.Settings.Mqtt;

        (await tester.TestAsync(mqtt, _state.Current.Settings.Device, null, Ct)).Message.ShouldStartWith("Could not connect to broker.local:1883");

        var noHost = new MqttSettings { Host = " " };
        (await tester.TestAsync(noHost, _state.Current.Settings.Device, null, Ct)).Message.ShouldBe("Enter the broker's address.");
    }

    [Fact]
    public async Task TestConnection_ForeignPassword_AsksToEnterItAgain()
    {
        var mqtt = new MqttSettings { Host = "broker.local", PasswordProtected = "from-another-pc" };

        var result = await new MqttConnectionTester(() => new FakeMqttTransport(), _protector).TestAsync(mqtt, new DeviceSettings { Id = "BOX1" }, null, Ct);

        result.Message.ShouldContain("Enter it again");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartWithWindows_IsApplied_AtStartAndOnChange(bool initial)
    {
        var settings = FakeSettingsState.Configured();
        settings.StartWithWindows = initial;
        var state = new FakeSettingsState(settings);
        var registration = new FakeRegistration();
        var sync = new StartWithWindowsSynchronizer(state, registration, NullLogger<StartWithWindowsSynchronizer>.Instance);

        await sync.StartAsync(Ct);
        var changed = FakeSettingsState.Configured();
        changed.StartWithWindows = !initial;
        state.Set(changed);
        state.Set(new AppSettings { StartWithWindows = initial }, ["broken"]);
        await sync.StopAsync(Ct);

        registration.Applied.ShouldBe([initial, !initial], "a broken file must not flip the entry");
    }

    [Fact]
    public async Task StartWithWindows_NotInstalled_LeavesTheEntryAlone()
    {
        var registration = new FakeRegistration { IsInstalled = false };
        var sync = new StartWithWindowsSynchronizer(_state, registration, NullLogger<StartWithWindowsSynchronizer>.Instance);

        await sync.StartAsync(Ct);
        _state.Set(FakeSettingsState.Configured());

        registration.Applied.ShouldBeEmpty();
    }

    private sealed class RecordingStore : ISettingsStore
    {
        public AppSettings? Saved { get; private set; }

        public bool Exists() => true;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Saved = settings;
            return Task.CompletedTask;
        }

        public Task<SettingsCompletion> AddMissingAsync(AppSettings current, CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsCompletion([], false, false));

        public string? GetSyntaxError() => null;
    }

    private sealed class FakeRegistration : IStartupRegistration
    {
        public bool IsInstalled { get; init; } = true;

        public List<bool> Applied { get; } = [];

        public bool Apply(bool startWithWindows)
        {
            Applied.Add(startWithWindows);
            return true;
        }
    }
}
