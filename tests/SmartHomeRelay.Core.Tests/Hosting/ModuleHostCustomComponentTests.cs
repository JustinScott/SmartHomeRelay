using Microsoft.Extensions.Logging.Abstractions;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Hosting;

/// <summary>Spec 3.3: custom discovery components.</summary>
public sealed class ModuleHostCustomComponentTests : IDisposable
{
    private const string Root = "shr/DESKTOP-ABC123";
    private const string ConfigTopic = "homeassistant/gadget/DESKTOP-ABC123/config";

    private readonly FakeMqttTransport _transport = new();
    private readonly FakeSettingsState _settings = new(FakeSettingsState.Configured());
    private readonly FakeCustomModule _module;
    private readonly ModuleHost _host;

    public ModuleHostCustomComponentTests()
    {
        _module = new FakeCustomModule("gadgets") { Enabled = s => s.Features.Display.Enabled };
        _host = new ModuleHost([_module], _transport, _settings, NullLoggerFactory.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Enabled_PublishesTheModulesConfig_Retained()
    {
        await StartAndConnectAsync();

        var config = _transport.PublishedTo(ConfigTopic).ShouldHaveSingleItem();
        config.Retain.ShouldBeTrue();
        config.Payload.ShouldBe($"{{\"state\":\"{Root}/gadget/state\",\"go\":\"{Root}/gadget/command/go\"}}");
    }

    [Fact]
    public async Task Disabled_ClearsTheConfig_AndPublishesTheDisabledStates()
    {
        Disable();

        await StartAndConnectAsync();

        _transport.PublishedTo(ConfigTopic).ShouldHaveSingleItem().Payload.ShouldBeEmpty();
        _transport.PublishedTo($"{Root}/gadget/state").ShouldHaveSingleItem().Payload.ShouldBe("off");
        var title = _transport.PublishedTo($"{Root}/gadget/title").ShouldHaveSingleItem();
        title.Payload.ShouldBeEmpty();
        title.Retain.ShouldBeTrue();
    }

    [Fact]
    public async Task States_AreKept_AndRepublishedOnReconnect()
    {
        await _host.StartAsync(Ct);
        await _module.Context!.PublishCustomStateAsync("gadget", "state", "playing", Ct);
        _transport.Publications.ShouldBeEmpty();

        await ConnectAsync();
        _transport.Drop();
        await ConnectAsync();

        _transport.PublishedTo($"{Root}/gadget/state").Select(p => p.Payload).ShouldBe(["playing", "playing"]);
    }

    [Fact]
    public async Task UnknownState_IsIgnored()
    {
        await StartAndConnectAsync();

        await _module.Context!.PublishCustomStateAsync("gadget", "volume", "1", Ct);
        await _module.Context!.PublishCustomStateAsync("other", "state", "x", Ct);

        _transport.Publications.ShouldNotContain(p => p.Topic.EndsWith("/volume", StringComparison.Ordinal) || p.Topic.Contains("/other/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Commands_AreSubscribed_AndRouted()
    {
        await StartAndConnectAsync();

        _transport.Subscriptions.ShouldContain($"{Root}/gadget/command/go");
        _transport.Receive($"{Root}/gadget/command/go", "Play");
        _transport.Receive($"{Root}/gadget/command/nope", "x");
        await _host.WhenBackgroundIdleAsync();

        _module.Commands.ShouldBe([("go", "Play")]);
    }

    [Fact]
    public async Task Commands_ForADisabledModule_AreIgnored()
    {
        Disable();
        await StartAndConnectAsync();

        _transport.Receive($"{Root}/gadget/command/go", "Play");
        await _host.WhenBackgroundIdleAsync();

        _module.Commands.ShouldBeEmpty();
    }

    [Fact]
    public async Task TurningOff_ForgetsTheStates_AndPublishesTheDisabledOnes()
    {
        await StartAndConnectAsync();
        await _module.Context!.PublishCustomStateAsync("gadget", "title", "Song", Ct);

        Disable();
        await _host.WhenBackgroundIdleAsync();

        _transport.PublishedTo(ConfigTopic).Select(p => p.Payload).Last().ShouldBeEmpty();
        _transport.PublishedTo($"{Root}/gadget/title").Select(p => p.Payload).ShouldBe(["Song", string.Empty]);
    }

    [Fact]
    public void NameCollidingWithAnEntityKey_RejectsTheModule()
    {
        var module = new FakeCustomModule("clash", "uptime", FakeModule.Sensor("uptime"));

        var catalog = EntityCatalog.Build([module]);

        catalog.Modules.ShouldBeEmpty();
        catalog.Rejections.ShouldContain(r => r.Contains("same name as an entity", StringComparison.Ordinal));
    }

    [Fact]
    public void NameCollidingWithAnotherModulesEntity_RejectsTheLaterModule()
    {
        var catalog = EntityCatalog.Build([new FakeModule("first", FakeModule.Sensor("gadget")), new FakeCustomModule("second")]);

        catalog.Modules.Select(m => m.Id).ShouldBe(["first"]);
    }

    [Fact]
    public void Topics_ParseOnlyCustomCommandTopics()
    {
        var settings = FakeSettingsState.Configured();
        var topics = new MqttTopics(settings.Mqtt, settings.Device);

        topics.TryGetCustomCommand($"{Root}/media_player/command/play", out var component, out var command).ShouldBeTrue();
        (component, command).ShouldBe(("media_player", "play"));
        topics.TryGetCustomCommand($"{Root}/media_player/state", out _, out _).ShouldBeFalse();
        topics.TryGetCustomCommand($"{Root}/volume/set", out _, out _).ShouldBeFalse();
        topics.TryGetCustomCommand($"{Root}/media_player/command/play/x", out _, out _).ShouldBeFalse();
        topics.TryGetCustomCommand("shr/OTHER/media_player/command/play", out _, out _).ShouldBeFalse();
    }

    private void Disable()
    {
        var settings = FakeSettingsState.Configured();
        settings.Features.Display.Enabled = false;
        _settings.Set(settings);
    }

    private async Task StartAndConnectAsync()
    {
        await _host.StartAsync(Ct);
        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        var settings = _settings.Current.Settings;
        var topics = new MqttTopics(settings.Mqtt, settings.Device);
        await _transport.ConnectAsync(new MqttConnectionOptions { Host = "h", Port = 1, ClientId = "c", WillTopic = topics.Availability }, Ct);
        await _host.OnConnectedAsync(topics, settings, Ct);
    }
}
