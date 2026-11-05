using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Configuration;

public sealed class SettingsValidatorTests
{
    private readonly SettingsValidator _validator = new();

    [Fact]
    public void Validate_DefaultsWithDeviceIdentity_ReturnsNoErrors()
    {
        _validator.Validate(TestSettings.Valid()).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_EmptyHost_IsAllowedBecauseItMeansNotConfigured()
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.Host = string.Empty;

        _validator.Validate(settings).ShouldBeEmpty();
        settings.Mqtt.IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public void Validate_HostWithSpace_ReturnsHostError()
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.Host = "192.168.1.10 ";

        ShouldHaveSingleErrorFor(settings, "mqtt.host");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65_536)]
    public void Validate_PortOutOfRange_ReturnsPortError(int port)
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.Port = port;

        ShouldHaveSingleErrorFor(settings, "mqtt.port");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1883)]
    [InlineData(65_535)]
    public void Validate_PortInRange_ReturnsNoErrors(int port)
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.Port = port;

        _validator.Validate(settings).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_UseTlsTrue_ReturnsErrorBecauseTlsIsNotSupportedInV1()
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.UseTls = true;

        ShouldHaveSingleErrorFor(settings, "mqtt.useTls");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("shr/#")]
    [InlineData("shr/+")]
    [InlineData("/shr")]
    [InlineData("shr/")]
    public void Validate_InvalidBaseTopic_ReturnsBaseTopicError(string baseTopic)
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.BaseTopic = baseTopic;

        _validator.Validate(settings).ShouldContain(e => e.StartsWith("mqtt.baseTopic", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NestedBaseTopic_IsAllowed()
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.BaseTopic = "home/shr";

        _validator.Validate(settings).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("homeassistant/")]
    public void Validate_InvalidDiscoveryPrefix_ReturnsDiscoveryPrefixError(string prefix)
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.DiscoveryPrefix = prefix;

        _validator.Validate(settings).ShouldContain(e => e.StartsWith("mqtt.discoveryPrefix", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("my pc")]
    [InlineData("pc/1")]
    [InlineData("pc+")]
    [InlineData("pc#")]
    [InlineData("pc.local")]
    [InlineData("pcé")]
    public void Validate_InvalidDeviceId_ReturnsDeviceIdError(string id)
    {
        var settings = TestSettings.Valid();
        settings.Device.Id = id;

        ShouldHaveSingleErrorFor(settings, "device.id");
    }

    [Theory]
    [InlineData("DESKTOP-ABC123")]
    [InlineData("desktop_main")]
    public void Validate_HostNameStyleDeviceId_ReturnsNoErrors(string id)
    {
        var settings = TestSettings.Valid();
        settings.Device.Id = id;

        _validator.Validate(settings).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_EmptyDeviceName_ReturnsDeviceNameError()
    {
        var settings = TestSettings.Valid();
        settings.Device.Name = " ";

        ShouldHaveSingleErrorFor(settings, "device.name");
    }

    [Theory]
    [InlineData(9)]
    [InlineData(86_401)]
    public void Validate_IdleThresholdOutOfRange_ReturnsError(int seconds)
    {
        var settings = TestSettings.Valid();
        settings.Features.Presence.IdleThresholdSeconds = seconds;

        ShouldHaveSingleErrorFor(settings, "features.presence.idleThresholdSeconds");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(601)]
    public void Validate_ShutdownDelayOutOfRange_ReturnsError(int seconds)
    {
        var settings = TestSettings.Valid();
        settings.Features.Power.ShutdownDelaySeconds = seconds;

        ShouldHaveSingleErrorFor(settings, "features.power.shutdownDelaySeconds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(600)]
    public void Validate_ShutdownDelayAtBounds_ReturnsNoErrors(int seconds)
    {
        var settings = TestSettings.Valid();
        settings.Features.Power.ShutdownDelaySeconds = seconds;

        _validator.Validate(settings).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(3_601)]
    public void Validate_MetricsIntervalOutOfRange_ReturnsError(int seconds)
    {
        var settings = TestSettings.Valid();
        settings.Features.Metrics.IntervalSeconds = seconds;

        ShouldHaveSingleErrorFor(settings, "features.metrics.intervalSeconds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Validate_ImageTimeoutOutOfRange_ReturnsError(int seconds)
    {
        var settings = TestSettings.Valid();
        settings.Features.Notifications.ImageDownloadTimeoutSeconds = seconds;

        ShouldHaveSingleErrorFor(settings, "features.notifications.imageDownloadTimeoutSeconds");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData((100L * 1024 * 1024) + 1)]
    public void Validate_ImageMaxBytesOutOfRange_ReturnsError(long bytes)
    {
        var settings = TestSettings.Valid();
        settings.Features.Notifications.ImageMaxBytes = bytes;

        ShouldHaveSingleErrorFor(settings, "features.notifications.imageMaxBytes");
    }

    [Theory]
    [InlineData(LogLevel.None)]
    [InlineData((LogLevel)42)]
    public void Validate_UnusableLogLevel_ReturnsError(LogLevel level)
    {
        var settings = TestSettings.Valid();
        settings.LogLevel = level;

        ShouldHaveSingleErrorFor(settings, "logLevel");
    }

    [Fact]
    public void Validate_SeveralProblems_ReportsEachOne()
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.Port = 0;
        settings.Device.Id = string.Empty;
        settings.Features.Metrics.IntervalSeconds = 0;

        _validator.Validate(settings).Count.ShouldBe(3);
    }

    [Fact]
    public void Validate_Null_Throws()
    {
        Should.Throw<ArgumentNullException>(() => _validator.Validate(null!));
    }

    private void ShouldHaveSingleErrorFor(AppSettings settings, string jsonPath)
    {
        var errors = _validator.Validate(settings);
        errors.Count.ShouldBe(1, string.Join(Environment.NewLine, errors));
        errors[0].ShouldStartWith(jsonPath + " ");
    }

    [Fact]
    public void Validate_HardwareDefaults_AreValid()
    {
        var settings = TestSettings.Valid();
        settings.Features.Hardware.Enabled = true;

        new SettingsValidator().Validate(settings).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_MediaPlayerDefaults_AreValid()
    {
        var settings = TestSettings.Valid();
        settings.Features.MediaPlayer.Enabled = true;

        new SettingsValidator().Validate(settings).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Duck")]
    [InlineData("mute")]
    [InlineData("")]
    public void Validate_MediaPlayerInterruption_MustBeDuckOrPause(string value)
    {
        var settings = TestSettings.Valid();
        settings.Features.MediaPlayer.Interruption = value;

        new SettingsValidator().Validate(settings).ShouldContain(e => e.StartsWith("features.mediaPlayer.interruption", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("duckLevelPercent")]
    [InlineData("maxDownloadBytes")]
    [InlineData("downloadTimeoutSeconds")]
    [InlineData("positionIntervalSeconds")]
    public void Validate_MediaPlayerNumbers_OutOfRange(string name)
    {
        var settings = TestSettings.Valid();
        var media = settings.Features.MediaPlayer;
        switch (name)
        {
            case "duckLevelPercent": media.DuckLevelPercent = 101; break;
            case "maxDownloadBytes": media.MaxDownloadBytes = 0; break;
            case "downloadTimeoutSeconds": media.DownloadTimeoutSeconds = 0; break;
            default: media.PositionIntervalSeconds = 0; break;
        }

        new SettingsValidator().Validate(settings).ShouldContain(e => e.StartsWith($"features.mediaPlayer.{name}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, "features.hardware.port")]
    [InlineData(70_000, "features.hardware.port")]
    public void Validate_HardwarePortOutOfRange(int port, string error)
    {
        var settings = TestSettings.Valid();
        settings.Features.Hardware.Port = port;

        new SettingsValidator().Validate(settings).ShouldContain(e => e.StartsWith(error, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_HardwareUnknownOrDuplicateSensor()
    {
        var settings = TestSettings.Valid();
        settings.Features.Hardware.Sensors = ["cpu_temperature", "gpu_hotspot", "cpu_temperature"];

        var errors = new SettingsValidator().Validate(settings);

        errors.ShouldContain(e => e.Contains("unknown sensor 'gpu_hotspot'", StringComparison.Ordinal));
        errors.ShouldContain(e => e.Contains("'cpu_temperature' more than once", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("cpu_fan", "fan/1")]
    [InlineData("cpu_fan", "/lpc/x 0/fan/1")]
    [InlineData("case_fan", "/lpc/nct6798d/0/fan/1")]
    public void Validate_HardwareBadOverride(string key, string sensorId)
    {
        var settings = TestSettings.Valid();
        settings.Features.Hardware.SensorOverrides[key] = sensorId;

        new SettingsValidator().Validate(settings).ShouldContain(e => e.StartsWith("features.hardware.sensorOverrides", StringComparison.Ordinal));
    }
}
