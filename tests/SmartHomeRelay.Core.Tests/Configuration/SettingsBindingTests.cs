using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Configuration;

public sealed class SettingsBindingTests : IDisposable
{
    // The example from spec section 7, plus logLevel.
    private const string SpecSample = """
        {
          "mqtt": {
            "host": "192.168.1.10",
            "port": 1883,
            "useTls": false,
            "username": "winbridge",
            "passwordProtected": "<dpapi blob>",
            "discoveryPrefix": "homeassistant",
            "baseTopic": "shr"
          },
          "device": { "id": "desktop_main", "name": "Main Desktop" },
          "features": {
            "presence": { "enabled": true, "idleThresholdSeconds": 300 },
            "power":    { "enabled": true, "allowShutdown": false, "allowRestart": false, "allowHibernate": false, "allowSignOut": false, "shutdownDelaySeconds": 90 },
            "display":  { "enabled": true },
            "audio":    { "enabled": false },
            "metrics":  { "enabled": true, "intervalSeconds": 45 },
            "notifications": { "enabled": true, "imageDownloadTimeoutSeconds": 10, "imageMaxBytes": 10485760 }
          },
          "startWithWindows": true,
          "logLevel": "Debug"
        }
        """;

    private readonly TempAppPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void Bind_HardwareSensors_ReplacesTheDefaultListInsteadOfAppending()
    {
        // The configuration binder adds to a list that already has items; the defaults must not leak in.
        _paths.WriteSettingsFile("""
            { "features": { "hardware": { "enabled": true, "sensors": ["cpu_fan"], "sensorOverrides": { "cpu_fan": "/lpc/nct6798d/0/fan/1" } } } }
            """);
        using var context = new SettingsTestContext(_paths);

        var hardware = context.Services.GetRequiredService<IOptionsMonitor<AppSettings>>().CurrentValue.Features.Hardware;

        hardware.EffectiveSensors.ShouldBe(["cpu_fan"]);
        hardware.SensorOverrides["cpu_fan"].ShouldBe("/lpc/nct6798d/0/fan/1");
    }

    [Fact]
    public void Bind_NoHardwareSection_IsOffWithDefaults()
    {
        _paths.WriteSettingsFile(SpecSample);
        using var context = new SettingsTestContext(_paths);

        var hardware = context.Services.GetRequiredService<IOptionsMonitor<AppSettings>>().CurrentValue.Features.Hardware;

        hardware.Enabled.ShouldBeFalse("spec 5.8: off by default, so it never competes with HA's own integration");
        hardware.Port.ShouldBe(8085);
        hardware.EffectiveSensors.ShouldBe(["cpu_temperature", "cpu_fan", "ssd_temperature"]);
    }

    [Fact]
    public void Bind_SpecSample_ReadsEveryValue()
    {
        _paths.WriteSettingsFile(SpecSample);
        using var context = new SettingsTestContext(_paths);

        var settings = context.Services.GetRequiredService<IOptionsMonitor<AppSettings>>().CurrentValue;

        settings.Mqtt.Host.ShouldBe("192.168.1.10");
        settings.Mqtt.Port.ShouldBe(1883);
        settings.Mqtt.UseTls.ShouldBeFalse();
        settings.Mqtt.Username.ShouldBe("winbridge");
        settings.Mqtt.PasswordProtected.ShouldBe("<dpapi blob>");
        settings.Mqtt.DiscoveryPrefix.ShouldBe("homeassistant");
        settings.Mqtt.BaseTopic.ShouldBe("shr");
        settings.Device.Id.ShouldBe("desktop_main");
        settings.Device.Name.ShouldBe("Main Desktop");
        settings.Features.Presence.Enabled.ShouldBeTrue();
        settings.Features.Presence.IdleThresholdSeconds.ShouldBe(300);
        settings.Features.Power.AllowShutdown.ShouldBeFalse();
        settings.Features.Power.ShutdownDelaySeconds.ShouldBe(90);
        settings.Features.Audio.Enabled.ShouldBeFalse();
        settings.Features.Metrics.IntervalSeconds.ShouldBe(45);
        settings.Features.Notifications.ImageMaxBytes.ShouldBe(10_485_760);
        settings.StartWithWindows.ShouldBeTrue();
        settings.LogLevel.ShouldBe(LogLevel.Debug);
    }

    [Fact]
    public void Bind_KeysInDifferentCase_StillBind()
    {
        _paths.WriteSettingsFile("""{ "MQTT": { "Host": "broker.local" } }""");
        using var context = new SettingsTestContext(_paths);

        context.Services.GetRequiredService<IOptionsMonitor<AppSettings>>().CurrentValue.Mqtt.Host.ShouldBe("broker.local");
    }

    [Fact]
    public void Bind_NoFile_UsesDefaultsAndHostNameIdentity()
    {
        using var context = new SettingsTestContext(_paths);

        var settings = context.Services.GetRequiredService<IOptionsMonitor<AppSettings>>().CurrentValue;

        settings.Mqtt.Host.ShouldBeEmpty();
        settings.Mqtt.Port.ShouldBe(1883);
        settings.Mqtt.BaseTopic.ShouldBe("shr");
        settings.Mqtt.DiscoveryPrefix.ShouldBe("homeassistant");
        settings.Device.Id.ShouldBe(SettingsTestContext.HostName);
        settings.Device.Name.ShouldBe(SettingsTestContext.HostName);
        settings.Features.Presence.IdleThresholdSeconds.ShouldBe(300);
        settings.Features.Metrics.IntervalSeconds.ShouldBe(30);
        settings.Features.Power.ShutdownDelaySeconds.ShouldBe(60);
        settings.Features.Notifications.ImageDownloadTimeoutSeconds.ShouldBe(10);
        settings.LogLevel.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public async Task SaveAsync_ThenBind_RoundTripsEveryValue()
    {
        var original = TestSettings.Valid();
        original.Mqtt.Host = "broker.local";
        original.Mqtt.Port = 1884;
        original.Mqtt.Username = "relay";
        original.Mqtt.PasswordProtected = "AQAAANCMnd8BFdERjHoAwE";
        original.Mqtt.BaseTopic = "home/shr";
        original.Features.Power.AllowRestart = true;
        original.Features.Display.Enabled = false;
        original.Features.Metrics.IntervalSeconds = 60;
        original.StartWithWindows = false;
        original.LogLevel = LogLevel.Warning;

        await new JsonSettingsStore(_paths).SaveAsync(original, TestContext.Current.CancellationToken);
        using var context = new SettingsTestContext(_paths);
        var loaded = context.Services.GetRequiredService<IOptionsMonitor<AppSettings>>().CurrentValue;

        loaded.Mqtt.Host.ShouldBe("broker.local");
        loaded.Mqtt.Port.ShouldBe(1884);
        loaded.Mqtt.Username.ShouldBe("relay");
        loaded.Mqtt.PasswordProtected.ShouldBe("AQAAANCMnd8BFdERjHoAwE");
        loaded.Mqtt.BaseTopic.ShouldBe("home/shr");
        loaded.Device.Id.ShouldBe("TEST-PC");
        loaded.Device.Name.ShouldBe("Test PC");
        loaded.Features.Power.AllowRestart.ShouldBeTrue();
        loaded.Features.Display.Enabled.ShouldBeFalse();
        loaded.Features.Metrics.IntervalSeconds.ShouldBe(60);
        loaded.StartWithWindows.ShouldBeFalse();
        loaded.LogLevel.ShouldBe(LogLevel.Warning);
    }
}
