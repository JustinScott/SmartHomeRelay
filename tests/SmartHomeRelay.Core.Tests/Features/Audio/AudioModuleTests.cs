using Microsoft.Extensions.Time.Testing;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features.Audio;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Audio;

public sealed class AudioModuleTests : IAsyncDisposable
{
    private readonly FakeAudioController _audio = new();
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingModuleContext _context = new();
    private readonly AudioModule _module;

    public AudioModuleTests()
    {
        _module = new AudioModule(_audio, _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _module.StopAsync(CancellationToken.None);
        _module.Dispose();
    }

    [Fact]
    public void DescribeEntities_SpecKeys()
    {
        var entities = _module.DescribeEntities();

        entities.Select(e => e.Key).ShouldBe(["volume", "muted", "mic_muted", "output_device"]);
        entities[0].Mode.ShouldBe(NumberMode.Slider);
        entities[0].Range.ShouldBe(new NumberRange(0, 100, 1));
        entities[3].Component.ShouldBe(EntityComponent.Select);
    }

    [Fact]
    public async Task Start_PublishesStateAndRealDeviceOptions()
    {
        await _module.StartAsync(_context, Ct);

        _context.Latest("volume").ShouldBe("40");
        _context.Latest("muted").ShouldBe("OFF");
        _context.Latest("mic_muted").ShouldBe("OFF");
        _context.Latest("output_device").ShouldBe("Speakers (Realtek(R) Audio)");
        _context.Updates.ShouldHaveSingleItem().Options.ShouldBe(["Speakers (Realtek(R) Audio)", "Headset (USB Audio)"]);
    }

    [Fact]
    public async Task NoDevices_PublishesUnknownAndAPlaceholderOption()
    {
        _audio.State = new(null, null, null, [], null);

        await _module.StartAsync(_context, Ct);

        _context.Latest("volume").ShouldBe(AudioModule.UnknownPayload);
        _context.Latest("muted").ShouldBe(AudioModule.UnknownPayload);
        _context.Latest("output_device").ShouldBe(AudioDeviceNames.NoDevice);
        _context.Updates.ShouldBeEmpty("the placeholder option is already the initial one");
    }

    [Theory]
    [InlineData("25", "volume:0.25")]
    [InlineData("25.0", "volume:0.25")]
    [InlineData("150", "volume:1")]
    [InlineData("-3", "volume:0")]
    public async Task VolumeCommand_SetsTheLevel(string payload, string call)
    {
        await _module.StartAsync(_context, Ct);

        await Handler("volume")(payload, Ct);

        _audio.Calls.ShouldBe([call]);
    }

    [Theory]
    [InlineData("loud")]
    [InlineData("NaN")]
    public async Task VolumeCommand_Garbage_IsIgnored(string payload)
    {
        await _module.StartAsync(_context, Ct);

        await Handler("volume")(payload, Ct);

        _audio.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("muted", "ON", "muted:True")]
    [InlineData("muted", "off", "muted:False")]
    [InlineData("mic_muted", "ON", "mic:True")]
    public async Task SwitchCommands_SetMute(string key, string payload, string call)
    {
        await _module.StartAsync(_context, Ct);

        await Handler(key)(payload, Ct);

        _audio.Calls.ShouldBe([call]);
    }

    [Fact]
    public async Task OutputDeviceCommand_SwitchesTheDefaultById()
    {
        await _module.StartAsync(_context, Ct);

        await Handler("output_device")("Headset (USB Audio)", Ct);

        _audio.Calls.ShouldBe(["default:{headset}"]);
        await RefreshUntilAsync(() => _context.Latest("output_device") == "Headset (USB Audio)");
    }

    [Fact]
    public async Task OutputDeviceCommand_UnknownOption_IsIgnoredAndTheRealDefaultRepublished()
    {
        await _module.StartAsync(_context, Ct);
        var before = _context.Count("output_device");

        await Handler("output_device")("Bluetooth thing that left", Ct);

        _audio.Calls.ShouldBeEmpty();
        await RefreshUntilAsync(() => _context.Count("output_device") == before + 1);
        _context.Latest("output_device").ShouldBe("Speakers (Realtek(R) Audio)");
    }

    [Fact]
    public async Task DevicePluggedIn_UpdatesTheSelectOptions()
    {
        await _module.StartAsync(_context, Ct);
        var dock = new Core.Abstractions.AudioDevice("{dock}", "Dock speakers");

        _audio.Change(_audio.State with { OutputDevices = [.. _audio.State.OutputDevices, dock] });

        await RefreshUntilAsync(() => _context.Updates.Count == 2);
        _context.Updates.Last().Options.ShouldBe(["Speakers (Realtek(R) Audio)", "Headset (USB Audio)", "Dock speakers"]);
    }

    [Fact]
    public async Task ExternalVolumeChange_IsPublished_OnlyWhenTheValueChanges()
    {
        await _module.StartAsync(_context, Ct);

        _audio.Change(_audio.State with { Volume = 0.404 }); // still 40%
        _audio.Change(_audio.State with { Volume = 0.55 });

        await RefreshUntilAsync(() => _context.Latest("volume") == "55");
        _context.Count("volume").ShouldBe(2);
        _context.Count("muted").ShouldBe(1);
    }

    [Fact]
    public async Task ReadFailure_IsLogged_AndTheModuleKeepsWorking()
    {
        _audio.ThrowOnRead = new InvalidOperationException("audio service restarting");
        await _module.StartAsync(_context, Ct);
        _context.Published.ShouldBeEmpty();

        _audio.ThrowOnRead = null;
        _audio.Change(_audio.State);

        await RefreshUntilAsync(() => _context.Latest("volume") == "40");
    }

    [Fact]
    public async Task SafetyRefresh_RereadsWithoutANotification()
    {
        await _module.StartAsync(_context, Ct);
        var reads = _audio.Reads;

        await Eventually.TrueAsync(() => _audio.Reads > reads, "the periodic refresh runs", () => _time.Advance(TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData("volume")]
    [InlineData("muted")]
    [InlineData("mic_muted")]
    [InlineData("output_device")]
    public void Discovery_MatchesApprovedSnapshot(string key)
    {
        var settings = FakeSettingsState.Configured();
        var entity = _module.DescribeEntities().Single(e => e.Key == key);

        Snapshot.ShouldMatchApproved(DiscoveryPayloadBuilder.Build(entity, new MqttTopics(settings.Mqtt, settings.Device), settings.Device, "1.2.3"), $"audio_{key}");
    }

    private EntityCommandHandler Handler(string key) => _module.DescribeEntities().Single(e => e.Key == key).CommandHandler!;

    /// <summary>Refreshes are debounced on the fake clock; keep advancing it until the condition holds.</summary>
    private Task RefreshUntilAsync(Func<bool> condition) =>
        Eventually.TrueAsync(condition, "the debounced refresh publishes", () => _time.Advance(AudioModule.Debounce));
}
