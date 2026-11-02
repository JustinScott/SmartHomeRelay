using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Hosting;

public sealed class RelayStatusDescriberTests
{
    [Fact]
    public void FromSettings_NoHost_ReturnsNotConfigured()
    {
        var snapshot = new SettingsSnapshot(TestSettings.Valid(), []);

        RelayStatusDescriber.FromSettings(snapshot).ShouldBe(RelayStatus.NotConfigured);
    }

    [Fact]
    public void FromSettings_HostSet_ReturnsDisconnected()
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.Host = "broker.local";

        RelayStatusDescriber.FromSettings(new SettingsSnapshot(settings, [])).ShouldBe(RelayStatus.Disconnected);
    }

    [Fact]
    public void FromSettings_Errors_ReturnsSettingsInvalidEvenWhenHostIsSet()
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.Host = "broker.local";

        RelayStatusDescriber.FromSettings(new SettingsSnapshot(settings, ["mqtt.port must be between 1 and 65535."]))
            .ShouldBe(RelayStatus.SettingsInvalid);
    }

    [Fact]
    public void Describe_EveryStatus_HasShortText()
    {
        foreach (var status in Enum.GetValues<RelayStatus>())
        {
            var text = RelayStatusDescriber.Describe(status);
            text.ShouldNotBeNullOrWhiteSpace();

            // NotifyIcon.Text is limited to 127 characters including the "Smart Home Relay: " prefix.
            text.Length.ShouldBeLessThan(64);
        }
    }

    [Theory]
    [InlineData(RelayStatus.Connected, StatusSeverity.Ok)]
    [InlineData(RelayStatus.Connecting, StatusSeverity.Warning)]
    [InlineData(RelayStatus.Reconnecting, StatusSeverity.Warning)]
    [InlineData(RelayStatus.NotConfigured, StatusSeverity.Warning)]
    [InlineData(RelayStatus.Disconnected, StatusSeverity.Warning)]
    [InlineData(RelayStatus.SettingsInvalid, StatusSeverity.Error)]
    [InlineData(RelayStatus.Error, StatusSeverity.Error)]
    public void Severity_MapsEveryStatus(RelayStatus status, StatusSeverity expected)
    {
        RelayStatusDescriber.Severity(status).ShouldBe(expected);
    }

    [Fact]
    public void Describe_UnknownStatus_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RelayStatusDescriber.Describe((RelayStatus)99));
    }
}
