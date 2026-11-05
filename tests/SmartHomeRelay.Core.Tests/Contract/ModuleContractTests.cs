using System.Text.Json;
using SmartHomeRelay.Core.Features;
using SmartHomeRelay.Core.Features.Hardware;
using SmartHomeRelay.Core.Features.Metrics;
using SmartHomeRelay.Core.Features.Power;
using SmartHomeRelay.Core.Features.Presence;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Contract;

/// <summary>
/// Rules every real feature module must satisfy (spec 9, contract tests). When a module is added,
/// add it to <see cref="AllModules"/> (constructed with fakes) so it is checked here.
/// </summary>
public sealed class ModuleContractTests
{
    /// <summary>Every shipped module, constructed with fakes.</summary>
    private static IReadOnlyList<IFeatureModule> AllModules()
    {
        var sources = new FakePresenceSources();
        var power = new FakePowerController();
        var settings = new FakeSettingsState(FakeSettingsState.Configured());
        return
        [
            new PresenceModule(new FakeSystemEventSource(), sources, sources, settings, TimeProvider.System),
            new PowerModule(power, power, settings, TimeProvider.System),
            new MetricsModule(new FakeMetricsSource(), settings, TimeProvider.System),
            new SmartHomeRelay.Core.Features.Display.DisplayModule(new FakeDisplayController()),
            new SmartHomeRelay.Core.Features.Audio.AudioModule(new FakeAudioController(), TimeProvider.System),
            new HardwareModule(new FakeLhmEndpoint(), settings, new FakeSecretProtector(), new FakeUserNotifier(), TimeProvider.System),
            new SmartHomeRelay.Core.Features.MediaPlayer.MediaPlayerModule(
                settings, new FakeMediaSession(), new FakeAudioController(), new FakeAnnouncementPlayer(), new FakeAudioDucker(), new FakeHttpDownloader(), TimeProvider.System),
        ];
    }

    [Fact]
    public void AllModules_PassCatalogValidation()
    {
        var catalog = EntityCatalog.Build(AllModules());

        catalog.Rejections.ShouldBeEmpty();
        catalog.Modules.Count.ShouldBe(AllModules().Count);
    }

    [Fact]
    public void AllModules_DescribeTheSameEntitiesEveryTime()
    {
        foreach (var module in AllModules())
        {
            module.DescribeEntities().Select(e => e.Key).ShouldBe(module.DescribeEntities().Select(e => e.Key), module.Id);
        }
    }

    [Fact]
    public void AllModules_DiscoveryPayloadsAreValidJson()
    {
        var settings = FakeSettingsState.Configured();
        var topics = new MqttTopics(settings.Mqtt, settings.Device);

        foreach (var entry in EntityCatalog.Build(AllModules()).Entries)
        {
            using var document = JsonDocument.Parse(DiscoveryPayloadBuilder.Build(entry.Entity, topics, settings.Device, "1.0.0"));
            document.RootElement.GetProperty("unique_id").GetString().ShouldNotBeNullOrEmpty();
        }
    }

    [Fact]
    public void AllModules_CanBeDisabled()
    {
        // Every feature is individually toggleable (spec 5) so its discovery can be cleared.
        var allOff = TestSettings.Valid();
        allOff.Features.Presence.Enabled = false;
        allOff.Features.Power.Enabled = false;
        allOff.Features.Display.Enabled = false;
        allOff.Features.Audio.Enabled = false;
        allOff.Features.Metrics.Enabled = false;
        allOff.Features.Notifications.Enabled = false;
        allOff.Features.Hardware.Enabled = false;
        allOff.Features.MediaPlayer.Enabled = false;

        foreach (var module in AllModules())
        {
            module.IsEnabled(allOff).ShouldBeFalse(module.Id);
        }
    }

    [Fact]
    public void Rules_RejectAModuleThatBreaksThem()
    {
        // Guards the contract itself while AllModules is still empty.
        var broken = new FakeModule("broken", FakeModule.Sensor("dup"), FakeModule.Sensor("dup"));

        EntityCatalog.Build([broken]).Rejections.ShouldNotBeEmpty();
    }
}
