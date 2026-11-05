using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Hosting;

public sealed class ModuleHostTests : IDisposable
{
    private const string Root = "shr/DESKTOP-ABC123";

    private readonly FakeMqttTransport _transport = new();
    private readonly FakeSettingsState _settings = new(FakeSettingsState.Configured());
    private readonly ConcurrentQueue<string> _presses = new();
    private readonly FakeModule _metrics;
    private readonly FakeModule _power;
    private readonly ModuleHost _host;

    public ModuleHostTests()
    {
        _metrics = new FakeModule("metrics", FakeModule.Sensor("cpu_usage"))
        {
            Enabled = s => s.Features.Metrics.Enabled,
        };
        _power = new FakeModule("power", FakeModule.Button("sleep", _presses), FakeModule.Sensor("uptime"))
        {
            Enabled = s => s.Features.Power.Enabled,
        };
        _host = new ModuleHost([_metrics, _power], _transport, _settings, NullLoggerFactory.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Start_EnabledModules_AreStartedOnce()
    {
        await _host.StartAsync(Ct);

        _metrics.StartCount.ShouldBe(1);
        _power.StartCount.ShouldBe(1);
    }

    [Fact]
    public async Task Start_DisabledModule_IsNotStarted()
    {
        var settings = FakeSettingsState.Configured();
        settings.Features.Metrics.Enabled = false;
        _settings.Set(settings);

        await _host.StartAsync(Ct);

        _metrics.StartCount.ShouldBe(0);
        _power.StartCount.ShouldBe(1);
    }

    [Fact]
    public async Task Start_ModuleThrows_OtherModulesStillStart()
    {
        _metrics.ThrowOnStart = new InvalidOperationException("boom");

        await Should.NotThrowAsync(() => _host.StartAsync(Ct));

        _power.StartCount.ShouldBe(1);
    }

    [Fact]
    public async Task OnConnected_PublishesRetainedDiscoveryForEveryEnabledEntity()
    {
        await StartAndConnectAsync();

        var config = _transport.PublishedTo("homeassistant/sensor/DESKTOP-ABC123/cpu_usage/config").ShouldHaveSingleItem();
        config.Retain.ShouldBeTrue();
        config.Qos.ShouldBe(MqttQos.AtLeastOnce);
        config.Payload.ShouldContain("\"unique_id\":\"desktop_abc123_cpu_usage\"");
        _transport.PublishedTo("homeassistant/button/DESKTOP-ABC123/sleep/config").ShouldHaveSingleItem().Payload.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task OnConnected_DisabledModule_ClearsItsDiscoveryWithEmptyRetainedPayload()
    {
        var settings = FakeSettingsState.Configured();
        settings.Features.Power.Enabled = false;
        _settings.Set(settings);

        await StartAndConnectAsync();

        foreach (var topic in new[] { "homeassistant/button/DESKTOP-ABC123/sleep/config", "homeassistant/sensor/DESKTOP-ABC123/uptime/config" })
        {
            var cleared = _transport.PublishedTo(topic).ShouldHaveSingleItem();
            cleared.Payload.ShouldBeEmpty();
            cleared.Retain.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task OnConnected_SubscribesToHomeAssistantStatusAndCommandTopics()
    {
        await StartAndConnectAsync();

        _transport.Subscriptions.ShouldBe(["homeassistant/status", $"{Root}/sleep/set"], ignoreOrder: true);
    }

    [Fact]
    public async Task PublishState_WhileDisconnected_IsPublishedOnConnect()
    {
        await _host.StartAsync(Ct);
        await _metrics.Context!.PublishStateAsync("cpu_usage", "12", Ct);
        _transport.Publications.ShouldBeEmpty();

        await ConnectAsync();

        var state = _transport.PublishedTo($"{Root}/cpu_usage/state").ShouldHaveSingleItem();
        state.Payload.ShouldBe("12");
        state.Retain.ShouldBeTrue();
    }

    [Fact]
    public async Task PublishState_WhileConnected_PublishesImmediately()
    {
        await StartAndConnectAsync();

        await _metrics.Context!.PublishStateAsync("cpu_usage", "34", Ct);

        _transport.PublishedTo($"{Root}/cpu_usage/state").ShouldHaveSingleItem().Payload.ShouldBe("34");
    }

    [Fact]
    public async Task Reconnect_RepublishesLatestStateOnly()
    {
        await StartAndConnectAsync();
        await _metrics.Context!.PublishStateAsync("cpu_usage", "10", Ct);
        await _metrics.Context!.PublishStateAsync("cpu_usage", "20", Ct);

        _transport.Drop();
        await ConnectAsync();

        _transport.PublishedTo($"{Root}/cpu_usage/state").Select(p => p.Payload).ShouldBe(["10", "20", "20"]);
    }

    [Fact]
    public async Task PublishState_KeyOfAnotherModule_IsIgnored()
    {
        await StartAndConnectAsync();

        await _metrics.Context!.PublishStateAsync("uptime", "999", Ct);
        await _metrics.Context!.PublishStateAsync("not_an_entity", "1", Ct);

        _transport.PublishedTo($"{Root}/uptime/state").ShouldBeEmpty();
        _transport.PublishedTo($"{Root}/not_an_entity/state").ShouldBeEmpty();
    }

    [Fact]
    public async Task HomeAssistantOnline_RepublishesDiscoveryAndState()
    {
        await StartAndConnectAsync();
        await _metrics.Context!.PublishStateAsync("cpu_usage", "5", Ct);

        _transport.Receive("homeassistant/status", "online");
        await _host.WhenBackgroundIdleAsync();

        _transport.PublishedTo("homeassistant/sensor/DESKTOP-ABC123/cpu_usage/config").Count.ShouldBe(2);
        _transport.PublishedTo($"{Root}/cpu_usage/state").Count.ShouldBe(2);
    }

    [Fact]
    public async Task HomeAssistantOffline_DoesNotRepublish()
    {
        await StartAndConnectAsync();

        _transport.Receive("homeassistant/status", "offline");
        await _host.WhenBackgroundIdleAsync();

        _transport.PublishedTo("homeassistant/sensor/DESKTOP-ABC123/cpu_usage/config").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Command_RoutedToEntityHandler()
    {
        await StartAndConnectAsync();

        _transport.Receive($"{Root}/sleep/set", "PRESS");
        await _host.WhenBackgroundIdleAsync();

        _presses.ShouldBe(["PRESS"]);
    }

    [Fact]
    public async Task Command_SlowHandler_DoesNotBlockOtherCommands()
    {
        var release = new TaskCompletionSource();
        var fastPresses = new ConcurrentQueue<string>();
        var slow = new FakeModule(
            "slow",
            new EntityDescriptor("slow_button", "Slow", EntityComponent.Button) { CommandHandler = (_, ct) => release.Task.WaitAsync(ct) },
            FakeModule.Button("fast_button", fastPresses));
        using var host = new ModuleHost([slow], _transport, _settings, NullLoggerFactory.Instance);
        await host.StartAsync(Ct);
        await ConnectAsync(host);

        _transport.Receive($"{Root}/slow_button/set", "PRESS");
        _transport.Receive($"{Root}/fast_button/set", "PRESS");

        await Eventually.TrueAsync(() => fastPresses.Count == 1, "the fast command runs while the slow one is still busy");
        release.SetResult();
        await host.WhenBackgroundIdleAsync();
    }

    [Fact]
    public async Task Command_HandlerThrows_IsIsolated()
    {
        var presses = new ConcurrentQueue<string>();
        var faulty = new FakeModule(
            "faulty",
            new EntityDescriptor("broken", "Broken", EntityComponent.Button) { CommandHandler = (_, _) => throw new InvalidOperationException("boom") },
            FakeModule.Button("works", presses));
        using var host = new ModuleHost([faulty], _transport, _settings, NullLoggerFactory.Instance);
        await host.StartAsync(Ct);
        await ConnectAsync(host);

        _transport.Receive($"{Root}/broken/set", "PRESS");
        await host.WhenBackgroundIdleAsync();
        _transport.Receive($"{Root}/works/set", "PRESS");
        await host.WhenBackgroundIdleAsync();

        presses.ShouldBe(["PRESS"]);
    }

    [Fact]
    public async Task Command_ForDisabledModule_IsIgnored()
    {
        await StartAndConnectAsync();
        var settings = FakeSettingsState.Configured();
        settings.Features.Power.Enabled = false;
        _settings.Set(settings);
        await _host.WhenBackgroundIdleAsync();

        _transport.Receive($"{Root}/sleep/set", "PRESS");
        await _host.WhenBackgroundIdleAsync();

        _presses.ShouldBeEmpty();
    }

    [Fact]
    public async Task Command_ForOtherDevice_IsIgnored()
    {
        await StartAndConnectAsync();

        _transport.Receive("shr/OTHER-PC/sleep/set", "PRESS");
        await _host.WhenBackgroundIdleAsync();

        _presses.ShouldBeEmpty();
    }

    [Fact]
    public async Task SettingsChanged_FeatureDisabled_StopsModuleAndClearsDiscovery()
    {
        await StartAndConnectAsync();
        var settings = FakeSettingsState.Configured();
        settings.Features.Metrics.Enabled = false;

        _settings.Set(settings);
        await _host.WhenBackgroundIdleAsync();

        _metrics.StopCount.ShouldBe(1);
        _transport.PublishedTo("homeassistant/sensor/DESKTOP-ABC123/cpu_usage/config").Last().Payload.ShouldBeEmpty();
    }

    [Fact]
    public async Task SettingsChanged_FeatureEnabled_StartsModuleAndPublishesDiscovery()
    {
        var disabled = FakeSettingsState.Configured();
        disabled.Features.Metrics.Enabled = false;
        _settings.Set(disabled);
        await StartAndConnectAsync();

        _settings.Set(FakeSettingsState.Configured());
        await _host.WhenBackgroundIdleAsync();

        _metrics.StartCount.ShouldBe(1);
        _transport.PublishedTo("homeassistant/sensor/DESKTOP-ABC123/cpu_usage/config").Last().Payload.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task SettingsChanged_Invalid_LeavesModulesRunning()
    {
        await _host.StartAsync(Ct);

        var settings = FakeSettingsState.Configured();
        settings.Features.Metrics.Enabled = false;
        _settings.Set(settings, ["mqtt.port must be between 1 and 65535."]);
        await _host.WhenBackgroundIdleAsync();

        _metrics.StopCount.ShouldBe(0);
    }

    [Fact]
    public async Task Stop_StopsRunningModules()
    {
        await _host.StartAsync(Ct);

        await _host.StopAsync(Ct);

        _metrics.StopCount.ShouldBe(1);
        _power.StopCount.ShouldBe(1);
    }

    [Fact]
    public async Task Start_RejectedModule_IsNeitherStartedNorPublished()
    {
        var bad = new FakeModule("bad", new EntityDescriptor("Bad Key", "Bad", EntityComponent.Sensor));
        using var host = new ModuleHost([bad, _metrics], _transport, _settings, NullLoggerFactory.Instance);

        await host.StartAsync(Ct);
        await ConnectAsync(host);

        bad.StartCount.ShouldBe(0);
        host.Catalog.Rejections.ShouldHaveSingleItem();
        _transport.Publications.ShouldAllBe(p => !p.Topic.Contains("Bad", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateEntity_RepublishesItsDiscoveryRetained()
    {
        await StartAndConnectAsync();
        var updated = FakeModule.Sensor("cpu_usage") with { Name = "CPU load", UnitOfMeasurement = "%" };

        await _metrics.Context!.UpdateEntityAsync(updated, Ct);

        var configs = _transport.PublishedTo("homeassistant/sensor/DESKTOP-ABC123/cpu_usage/config");
        configs.Count.ShouldBe(2);
        configs[^1].Retain.ShouldBeTrue();
        configs[^1].Payload.ShouldContain("\"name\":\"CPU load\"");
        _host.Catalog.TryGet("cpu_usage", out var entry).ShouldBeTrue();
        entry.Entity.Name.ShouldBe("CPU load");
    }

    [Fact]
    public async Task UpdateEntity_IsKeptForTheNextConnect_WhenDisconnected()
    {
        await _host.StartAsync(Ct);

        await _metrics.Context!.UpdateEntityAsync(FakeModule.Sensor("cpu_usage") with { Name = "CPU load" }, Ct);
        await ConnectAsync();

        _transport.PublishedTo("homeassistant/sensor/DESKTOP-ABC123/cpu_usage/config").ShouldHaveSingleItem().Payload.ShouldContain("CPU load");
    }

    [Fact]
    public async Task UpdateEntity_InvalidChanges_AreRejected()
    {
        await StartAndConnectAsync();
        var before = _transport.Publications.Count;

        // Another module's key, a new key, a component change, and an invalid entity.
        await _metrics.Context!.UpdateEntityAsync(FakeModule.Sensor("uptime"), Ct);
        await _metrics.Context!.UpdateEntityAsync(FakeModule.Sensor("gpu_usage"), Ct);
        await _metrics.Context!.UpdateEntityAsync(new EntityDescriptor("cpu_usage", "CPU", EntityComponent.BinarySensor), Ct);
        await _metrics.Context!.UpdateEntityAsync(new EntityDescriptor("cpu_usage", " ", EntityComponent.Sensor), Ct);

        _transport.Publications.Count.ShouldBe(before);
        _host.Catalog.TryGet("cpu_usage", out var entry).ShouldBeTrue();
        entry.Entity.Name.ShouldBe("cpu usage");
    }

    private async Task StartAndConnectAsync()
    {
        await _host.StartAsync(Ct);
        await ConnectAsync();
    }

    private Task ConnectAsync() => ConnectAsync(_host);

    private async Task ConnectAsync(ModuleHost host)
    {
        var settings = _settings.Current.Settings;
        var topics = new MqttTopics(settings.Mqtt, settings.Device);
        await _transport.ConnectAsync(new MqttConnectionOptions { Host = "h", Port = 1, ClientId = "c", WillTopic = topics.Availability }, Ct);
        await host.OnConnectedAsync(topics, settings, Ct);
    }
}
