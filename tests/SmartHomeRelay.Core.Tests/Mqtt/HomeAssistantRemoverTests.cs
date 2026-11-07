using Microsoft.Extensions.Logging.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Mqtt;

/// <summary>Spec 4.4: Remove from Home Assistant.</summary>
public sealed class HomeAssistantRemoverTests
{
    private const string Ours = "{\"name\":\"X\",\"device\":{\"identifiers\":[\"DESKTOP-ABC123\"]}}";
    private const string Player = "{\"name\":\"P\",\"device\":{\"identifiers\":[\"DESKTOP-ABC123_media_player\"],\"via_device\":\"DESKTOP-ABC123\"}}";
    private const string Foreign = "{\"name\":\"Y\",\"device\":{\"identifiers\":[\"other-app-DESKTOP-ABC123\"]}}";

    private readonly FakeMqttTransport _transport = new();
    private readonly FakeSecretProtector _protector = new();
    private readonly AppSettings _settings = FakeSettingsState.Configured();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HomeAssistantRemover Remover() =>
        new(() => _transport, _protector, NullLogger<HomeAssistantRemover>.Instance, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2));

    [Fact]
    public async Task ClearsEverythingThisPcLeft_DiscoveryFirst_AvailabilityLast()
    {
        _transport.Retained["homeassistant/sensor/DESKTOP-ABC123/cpu_usage/config"] = Ours;
        _transport.Retained["homeassistant/sensor/DESKTOP-ABC123/old_key_from_v0/config"] = Ours;
        _transport.Retained["homeassistant/media_player/DESKTOP-ABC123/config"] = Player;
        _transport.Retained["shr/DESKTOP-ABC123/status"] = "offline";
        _transport.Retained["shr/DESKTOP-ABC123/cpu_usage/state"] = "12";
        _transport.Retained["shr/DESKTOP-ABC123/media_player/state"] = "off";
        _transport.Retained["shr/OTHER-PC/status"] = "online";
        _transport.Retained["homeassistant/sensor/OTHER-PC/cpu_usage/config"] = Ours;

        var result = await Remover().RemoveAsync(_settings, Ct);

        result.Outcome.ShouldBe(RemovalOutcome.Removed);
        result.EntitiesRemoved.ShouldBe(3);
        result.Message.ShouldContain("MQTT Media Player", Case.Sensitive, "the add-on keeps its player");
        _transport.Retained.Keys.ShouldBe(["shr/OTHER-PC/status", "homeassistant/sensor/OTHER-PC/cpu_usage/config"], ignoreOrder: true);

        var cleared = _transport.Publications.Select(p => p.Topic).ToList();
        _transport.Publications.ShouldAllBe(p => p.Payload.Length == 0 && p.Retain);
        cleared.Take(3).ShouldAllBe(t => t.StartsWith("homeassistant/", StringComparison.Ordinal));
        cleared[^1].ShouldBe("shr/DESKTOP-ABC123/status");
        _transport.DisconnectCount.ShouldBe(1);
    }

    [Fact]
    public async Task AnotherProgramsConfig_UnderTheSameName_IsKept()
    {
        _transport.Retained["homeassistant/sensor/DESKTOP-ABC123/cpu/config"] = Foreign;
        _transport.Retained["homeassistant/sensor/DESKTOP-ABC123/bad/config"] = "not json";

        var result = await Remover().RemoveAsync(_settings, Ct);

        result.EntitiesRemoved.ShouldBe(0);
        result.Message.ShouldContain("nothing to remove");
        _transport.Retained.Count.ShouldBe(2);
    }

    [Fact]
    public async Task UsesItsOwnClientId_AndAWillTheBrokerDoesNotKeep()
    {
        _settings.Mqtt.PasswordProtected = _protector.Protect("secret");

        await Remover().RemoveAsync(_settings, Ct);

        var options = _transport.ConnectAttempts.ShouldHaveSingleItem();
        options.ClientId.ShouldBe("shr-DESKTOP-ABC123-remove");
        options.Password.ShouldBe("secret");
        options.WillRetain.ShouldBeFalse();
        _transport.Subscriptions.ShouldBe(["homeassistant/+/DESKTOP-ABC123/#", "shr/DESKTOP-ABC123/#"]);
    }

    [Fact]
    public async Task NotSetUp_DoesNotConnect()
    {
        var result = await Remover().RemoveAsync(new AppSettings(), Ct);

        result.Outcome.ShouldBe(RemovalOutcome.NotConfigured);
        _transport.ConnectAttempts.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("BOX+1", "shr", "homeassistant")]
    [InlineData("BOX1", "shr/#", "homeassistant")]
    [InlineData("BOX1", "shr", "home+assistant")]
    public async Task WildcardsInSettings_AreRefused(string id, string baseTopic, string prefix)
    {
        _settings.Device.Id = id;
        _settings.Mqtt.BaseTopic = baseTopic;
        _settings.Mqtt.DiscoveryPrefix = prefix;

        (await Remover().RemoveAsync(_settings, Ct)).Outcome.ShouldBe(RemovalOutcome.Failed);
        _transport.ConnectAttempts.ShouldBeEmpty();
    }

    [Fact]
    public async Task UnreachableOrRefused_FailsWithoutThrowing_AndWithoutThePassword()
    {
        _settings.Mqtt.PasswordProtected = _protector.Protect("secret");
        _transport.FailNextConnects(1);
        _transport.FailNextConnects(1, new MqttConnectionRefusedException("bad user name or password"));

        var unreachable = await Remover().RemoveAsync(_settings, Ct);
        var refused = await Remover().RemoveAsync(_settings, Ct);

        unreachable.Outcome.ShouldBe(RemovalOutcome.Failed);
        unreachable.Message.ShouldStartWith("Could not reach the MQTT broker at broker.local:1883");
        refused.Message.ShouldContain("refused");
        refused.Message.ShouldNotContain("secret");
    }

    [Fact]
    public async Task PasswordFromAnotherPc_FailsWithoutConnecting()
    {
        _settings.Mqtt.PasswordProtected = "from-another-pc";

        (await Remover().RemoveAsync(_settings, Ct)).Message.ShouldContain("can't be read");
        _transport.ConnectAttempts.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("{\"device\":{\"identifiers\":[\"PC\"]}}", true)]
    [InlineData("{\"dev\":{\"ids\":\"PC\"}}", true)]
    [InlineData("{\"device\":{\"identifiers\":[\"PC_media_player\"]}}", true)]
    [InlineData("{\"device\":{\"identifiers\":[\"PC2\"]}}", false)]
    [InlineData("{\"device\":{\"identifiers\":[\"pc\"]}}", false)]
    [InlineData("{\"name\":\"no device\"}", false)]
    [InlineData("[]", false)]
    public void BelongsTo_ChecksTheDeviceIdentifier(string payload, bool expected) =>
        HomeAssistantRemover.BelongsTo(payload, "PC").ShouldBe(expected);
}
