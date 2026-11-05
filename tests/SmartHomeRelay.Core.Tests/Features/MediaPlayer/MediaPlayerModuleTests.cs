using Microsoft.Extensions.Time.Testing;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features.MediaPlayer;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.MediaPlayer;

/// <summary>Spec 5.9.</summary>
public sealed class MediaPlayerModuleTests : IAsyncDisposable
{
    private const string C = MediaPlayerKeys.Component;
    private const string Url = """{"media_type":"audio/mpeg","media_id":"http://ha.local:8123/api/tts_proxy/abc.mp3?authSig=secret"}""";

    private readonly FakeMediaSession _session = new();
    private readonly FakeAudioController _audio = new();
    private readonly FakeAnnouncementPlayer _player = new();
    private readonly FakeAudioDucker _ducker = new();
    private readonly FakeHttpDownloader _downloader = new();
    private readonly FakeToastPresenter _toasts = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeSettingsState _settings;
    private readonly RecordingModuleContext _context = new();
    private readonly MediaPlayerModule _module;

    public MediaPlayerModuleTests()
    {
        var settings = FakeSettingsState.Configured();
        settings.Features.MediaPlayer.Enabled = true;
        _settings = new FakeSettingsState(settings);
        _module = new MediaPlayerModule(_settings, _session, _audio, _player, _ducker, _downloader, _toasts, _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CustomCommandHandler Command => _module.DescribeCustomComponents().Single().CommandHandler!;

    public async ValueTask DisposeAsync()
    {
        await _module.StopAsync(CancellationToken.None);
        _module.Dispose();
    }

    [Fact]
    public void OffByDefault()
    {
        _module.IsEnabled(new AppSettings()).ShouldBeFalse();
        _module.DescribeEntities().ShouldBeEmpty();
    }

    [Fact]
    public void Component_HasTheSpecTopicsAndAddOnDiscoveryTopic()
    {
        var component = _module.DescribeCustomComponents().ShouldHaveSingleItem();

        component.Name.ShouldBe("media_player");
        component.DiscoveryTopic("BOX1").ShouldBe("homeassistant/media_player/BOX1/config");
        component.States.ShouldBe(["state", "title", "artist", "album", "duration", "position", "volume", "mute", "media_type"]);
        component.Commands.ShouldBe(["play", "pause", "playpause", "next", "previous", "volume", "mute", "seek", "play_media"]);
        component.DisabledStates["state"].ShouldBe("off");
        component.DisabledStates["title"].ShouldBeEmpty();
    }

    [Fact]
    public void Config_MatchesSnapshot()
    {
        var settings = FakeSettingsState.Configured();
        var topics = new CustomComponentTopics(new MqttTopics(settings.Mqtt, settings.Device), C, settings.Device, "1.0.0");

        Snapshot.ShouldMatchApproved(MediaPlayerConfig.Build(topics), "media_player");
    }

    [Fact]
    public void Config_HasItsOwnDevice_NeverSharingOurDevicesIdentifier()
    {
        // Found on BOX1: the add-on creates its own device, so a shared identifier gave two devices the same one.
        var settings = FakeSettingsState.Configured();
        var topics = new CustomComponentTopics(new MqttTopics(settings.Mqtt, settings.Device), C, settings.Device, "1.0.0");

        using var config = System.Text.Json.JsonDocument.Parse(MediaPlayerConfig.Build(topics));
        var device = config.RootElement.GetProperty("device");

        device.GetProperty("identifiers").EnumerateArray().Select(e => e.GetString()).ShouldBe([$"{settings.Device.Id}_media_player"]);
        device.GetProperty("name").GetString().ShouldBe($"{settings.Device.Name} Media Player");
        device.GetProperty("via_device").GetString().ShouldBe(settings.Device.Id, "linked to the PC's device");
        config.RootElement.GetProperty("name").GetString().ShouldBe($"{settings.Device.Name} Media Player");
    }

    [Fact]
    public async Task Start_MirrorsTheSession_AndTheVolume()
    {
        await _module.StartAsync(_context, Ct);

        _context.LatestCustom(C, "state").ShouldBe("playing");
        _context.LatestCustom(C, "title").ShouldBe("Song");
        _context.LatestCustom(C, "artist").ShouldBe("Artist");
        _context.LatestCustom(C, "album").ShouldBe("Album");
        _context.LatestCustom(C, "duration").ShouldBe("200");
        _context.LatestCustom(C, "position").ShouldBe("10");
        _context.LatestCustom(C, "media_type").ShouldBe("music");
        _context.LatestCustom(C, "volume").ShouldBe("0.40");
        _context.LatestCustom(C, "mute").ShouldBe("unmute");
        _ducker.Calls.ToArray().ShouldBe(new[] { "recover" }, customMessage: "levels left lowered by a crash come back at start");
    }

    [Fact]
    public async Task NoSession_IsIdle_WithEmptyMetadata()
    {
        _session.Current = null;

        await _module.StartAsync(_context, Ct);

        _context.LatestCustom(C, "state").ShouldBe("idle");
        _context.LatestCustom(C, "title").ShouldBeEmpty();
        _context.LatestCustom(C, "duration").ShouldBeEmpty();
        _context.LatestCustom(C, "position").ShouldBeEmpty();
    }

    [Fact]
    public async Task UnknownVolume_IsNotPublished()
    {
        // The add-on reads an empty volume as 0.
        _audio.State = new(null, null, null, [], null);

        await _module.StartAsync(_context, Ct);

        _context.LatestCustom(C, "volume").ShouldBeNull();
        _context.LatestCustom(C, "mute").ShouldBeNull();
    }

    [Theory]
    [InlineData(MediaPlaybackStatus.Playing, "paused", "playing")]
    [InlineData(MediaPlaybackStatus.Paused, "playing", "paused")]
    [InlineData(MediaPlaybackStatus.Stopped, "playing", "idle")]
    [InlineData(MediaPlaybackStatus.Closed, "playing", "idle")]
    [InlineData(MediaPlaybackStatus.Opened, "playing", "idle")]
    [InlineData(MediaPlaybackStatus.Changing, "playing", "playing")]
    [InlineData(MediaPlaybackStatus.Changing, "paused", "paused")]
    public void MapStatus(MediaPlaybackStatus status, string previous, string expected) =>
        MediaPlayerModule.MapStatus(status, previous).ShouldBe(expected);

    [Fact]
    public void Formats()
    {
        MediaPlayerModule.FormatVolume(0.456).ShouldBe("0.46");
        MediaPlayerModule.FormatVolume(1.2).ShouldBe("1.00");
        MediaPlayerModule.Seconds(TimeSpan.FromSeconds(12.9)).ShouldBe("12");
        MediaPlayerModule.Seconds(null).ShouldBeEmpty();
    }

    [Fact]
    public async Task Refresh_PublishesOnlyChanges()
    {
        await _module.StartAsync(_context, Ct);
        var count = _context.CustomPublished.Count;

        await _module.RefreshAsync(forcePosition: false, Ct);

        _context.CustomPublished.Count.ShouldBe(count);
    }

    [Fact]
    public async Task Position_IsPublishedOnSeekAndTrackChange_NotWhileItAdvancesNormally()
    {
        await _module.StartAsync(_context, Ct);

        _time.Advance(TimeSpan.FromSeconds(2));
        _session.Current = _session.Current! with { Position = TimeSpan.FromSeconds(12) };
        await _module.RefreshAsync(forcePosition: false, Ct);
        _context.LatestCustom(C, "position").ShouldBe("10", "normal progress isn't a jump");

        _session.Current = _session.Current with { Position = TimeSpan.FromSeconds(90) };
        await _module.RefreshAsync(forcePosition: false, Ct);
        _context.LatestCustom(C, "position").ShouldBe("90", "a seek is published at once");

        _session.Current = _session.Current with { Title = "Next song", Position = TimeSpan.FromSeconds(91) };
        await _module.RefreshAsync(forcePosition: false, Ct);
        _context.LatestCustom(C, "position").ShouldBe("91", "a new track is published at once");
    }

    [Fact]
    public async Task TransportCommands_GoToTheSession()
    {
        await _module.StartAsync(_context, Ct);

        await Command("play", "Play", Ct);
        await Command("pause", "Pause", Ct);
        await Command("playpause", "PlayPause", Ct);
        await Command("next", "Next", Ct);
        await Command("previous", "Previous", Ct);
        await Command("seek", "42.5", Ct);

        _session.Calls.ShouldBe(["Play", "Pause", "TogglePlayPause", "Next", "Previous", "Seek:42.5"]);
    }

    [Fact]
    public async Task VolumeAndMute_GoToTheDefaultOutput()
    {
        await _module.StartAsync(_context, Ct);

        await Command("volume", "0.25", Ct);
        await Command("mute", "mute", Ct);
        await Command("mute", "unmute", Ct);
        await Command("volume", "loud", Ct);
        await Command("seek", "-3", Ct);

        _audio.Calls.ShouldBe(["volume:0.25", "muted:True", "muted:False"]);
        _session.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlayMedia_DucksPlaysAndRestoresAfterTheDelay()
    {
        await _module.StartAsync(_context, Ct);
        _ducker.Calls.Clear();

        var playing = Command("play_media", Url, Ct);
        await Eventually.TrueAsync(() => _player.Started == 1, "the announcement starts");
        _ducker.Calls.ShouldBe(["duck:0.2"]);
        var request = _downloader.Requests.ShouldHaveSingleItem();
        request.Limits.MaxBytes.ShouldBe(MediaPlayerSettings.DefaultMaxDownloadBytes);
        request.Limits.Timeout.ShouldBe(TimeSpan.FromSeconds(15));
        request.Limits.AcceptedTypePrefix.ShouldBe("audio/");

        _player.Finish();
        await Eventually.TrueAsync(() => _player.Events.Contains("ended"), "the announcement ends");
        _ducker.Calls.ShouldNotContain("restore");
        await AdvanceUntilAsync(playing);

        _ducker.Calls.ShouldBe(["duck:0.2", "restore"]);
    }

    [Fact]
    public async Task PlayMedia_ShowsTheSpeakingNotice_AndRemovesItWhenDone()
    {
        await _module.StartAsync(_context, Ct);

        var playing = Command("play_media", Url, Ct);
        await Eventually.TrueAsync(() => _player.Started == 1, "the announcement starts");
        var notice = _toasts.Shown.ShouldHaveSingleItem();
        notice.Silent.ShouldBeTrue();
        notice.Tag.ShouldBe(MediaAnnouncer.NoticeTag);
        notice.Buttons.ShouldHaveSingleItem().OpensUrl.ShouldBeFalse();

        _player.Finish();
        await AdvanceUntilAsync(playing);

        _toasts.Removed.ShouldBe([(MediaAnnouncer.NoticeTag, MediaAnnouncer.NoticeGroup)]);
    }

    [Fact]
    public async Task PlayMedia_StopOnTheNotice_StopsAndRestores()
    {
        await _module.StartAsync(_context, Ct);
        _ducker.Calls.Clear();

        var playing = Command("play_media", Url, Ct);
        await Eventually.TrueAsync(() => _player.Started == 1, "the announcement starts");
        _toasts.Click(MediaAnnouncer.StopArguments);
        await playing;

        await Eventually.TrueAsync(() => _ducker.Calls.Contains("restore") && !_toasts.Removed.IsEmpty, "the duck and the notice end");
        _player.Events.ShouldContain("stopped");
    }

    [Fact]
    public async Task PlayMedia_NoticeTurnedOff_ShowsNothing()
    {
        var settings = FakeSettingsState.Configured();
        settings.Features.MediaPlayer.Enabled = true;
        settings.Features.MediaPlayer.ShowNotification = false;
        _settings.Set(settings);
        _player.Instant = true;
        await _module.StartAsync(_context, Ct);

        await AdvanceUntilAsync(Command("play_media", Url, Ct));

        _toasts.Shown.ShouldBeEmpty();
        _toasts.Removed.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlayMedia_BackToBack_KeepsOneNotice()
    {
        await _module.StartAsync(_context, Ct);

        var first = Command("play_media", Url, Ct);
        await Eventually.TrueAsync(() => _player.Started == 1, "the first starts");
        var second = Command("play_media", Url, Ct);
        await Eventually.TrueAsync(() => _player.Started == 2, "the second starts");
        await first;
        _player.Finish();
        await AdvanceUntilAsync(second);

        _toasts.Shown.Count.ShouldBe(1);
        _toasts.Removed.Count.ShouldBe(1);
    }

    [Fact]
    public async Task PlayMedia_ANewOne_ReplacesTheCurrent_AndKeepsTheDuck()
    {
        await _module.StartAsync(_context, Ct);
        _ducker.Calls.Clear();

        var first = Command("play_media", Url, Ct);
        await Eventually.TrueAsync(() => _player.Started == 1, "the first starts");
        var second = Command("play_media", Url, Ct);
        await Eventually.TrueAsync(() => _player.Started == 2, "the second starts");
        await first;
        _player.Events.ShouldContain("stopped");

        _player.Finish();
        await AdvanceUntilAsync(second);

        _ducker.Calls.ToArray().ShouldBe(new[] { "duck:0.2", "restore" }, customMessage: "one duck for both");
    }

    [Fact]
    public async Task PlayMedia_PauseMode_PausesAndResumesTheSession()
    {
        var settings = FakeSettingsState.Configured();
        settings.Features.MediaPlayer.Enabled = true;
        settings.Features.MediaPlayer.Interruption = "pause";
        _settings.Set(settings);
        _player.Instant = true;
        await _module.StartAsync(_context, Ct);

        await AdvanceUntilAsync(Command("play_media", Url, Ct));

        _session.Calls.ShouldBe(["Pause", "Play"]);
        _ducker.Calls.ShouldBe(["recover"]);
    }

    [Fact]
    public async Task PlayMedia_PauseMode_NothingPlaying_ResumesNothing()
    {
        var settings = FakeSettingsState.Configured();
        settings.Features.MediaPlayer.Enabled = true;
        settings.Features.MediaPlayer.Interruption = "pause";
        _settings.Set(settings);
        _session.Current = _session.Current! with { Status = MediaPlaybackStatus.Paused };
        _player.Instant = true;
        await _module.StartAsync(_context, Ct);

        await AdvanceUntilAsync(Command("play_media", Url, Ct));

        _session.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"media_type":"music","media_id":"file:///C:/Windows/win.ini"}""")]
    [InlineData("""{"media_type":"music","media_id":"C:\\Windows\\win.ini"}""")]
    [InlineData("""{"media_type":"music","media_id":"\\\\server\\share\\a.mp3"}""")]
    [InlineData("""{"media_type":"music","media_id":"ftp://ha/a.mp3"}""")]
    [InlineData("""{"media_type":"music","media_id":"/local/a.mp3"}""")]
    [InlineData("http://ha.local/a.mp3")]
    [InlineData("not json")]
    public async Task PlayMedia_AnythingButAnHttpUrl_IsRefused(string payload)
    {
        await _module.StartAsync(_context, Ct);

        await Command("play_media", payload, Ct);

        _downloader.Requests.ShouldBeEmpty();
        _player.Started.ShouldBe(0);
    }

    [Fact]
    public async Task PlayMedia_DownloadRejected_DoesNotDuckOrPlay()
    {
        _downloader.Reject = new SmartHomeRelay.Core.Net.DownloadRejectedException("The server sent 'text/html', not audio/*.");
        await _module.StartAsync(_context, Ct);
        _ducker.Calls.Clear();

        await AdvanceUntilAsync(Command("play_media", Url, Ct));

        _player.Started.ShouldBe(0);
        _ducker.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlayMedia_PlayerFails_StillRestores()
    {
        _player.Throw = new InvalidOperationException("Can't decode.");
        await _module.StartAsync(_context, Ct);
        _ducker.Calls.Clear();

        await AdvanceUntilAsync(Command("play_media", Url, Ct));

        _ducker.Calls.ShouldBe(["duck:0.2", "restore"]);
    }

    [Fact]
    public async Task Suspend_StopsTheAnnouncementAndRestores_ResumeRecovers()
    {
        await _module.StartAsync(_context, Ct);
        _ducker.Calls.Clear();
        var playing = Command("play_media", Url, Ct);
        await Eventually.TrueAsync(() => _player.Started == 1, "the announcement starts");

        await _module.OnSuspendingAsync(Ct);
        await playing;
        await _module.OnResumedAsync(Ct);

        _player.Events.ShouldContain("stopped");
        _ducker.Calls.ShouldBe(["duck:0.2", "restore", "recover"]);
    }

    [Theory]
    [InlineData("""{"media_type":"audio/mpeg","media_id":"https://ha.example.com/api/tts_proxy/x.mp3"}""", "https://ha.example.com/api/tts_proxy/x.mp3")]
    [InlineData("""{"media_id":"http://192.168.1.5:8123/media/local/chime.wav?authSig=x"}""", "http://192.168.1.5:8123/media/local/chime.wav?authSig=x")]
    public void ParseRequest_AcceptsHttpUrls(string payload, string expected) =>
        MediaAnnouncer.ParseRequest(payload)!.AbsoluteUri.ShouldBe(expected);

    /// <summary>Advances fake time in small steps until <paramref name="task"/> finishes (the restore delay).</summary>
    private async Task AdvanceUntilAsync(Task task)
    {
        for (var i = 0; i < 200 && !task.IsCompleted; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(5, Ct);
        }

        await task;
    }
}
