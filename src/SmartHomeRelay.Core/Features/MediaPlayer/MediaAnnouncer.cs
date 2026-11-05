using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Net;
using SmartHomeRelay.Core.Toasts;

namespace SmartHomeRelay.Core.Features.MediaPlayer;

/// <summary>
/// Plays what HA sends to <c>play_media</c> (spec 5.9): checks and downloads the URL, interrupts other audio
/// (duck or pause), plays, and undoes the interruption <see cref="RestoreDelay"/> after the last announcement.
/// One at a time: a new announcement stops the one playing and keeps the interruption. While it plays, a silent
/// "Home Assistant is speaking" toast with a Stop button can be shown (spec 5.6); it is removed with the interruption.
/// </summary>
public sealed partial class MediaAnnouncer : IDisposable
{
    /// <summary>How long after an announcement the other audio comes back, so back-to-back ones keep the duck.</summary>
    public static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>Only audio is played.</summary>
    public const string AcceptedTypePrefix = "audio/";

    /// <summary>The arguments of the speaking notice's Stop button.</summary>
    public const string StopArguments = "shr:media_player:stop";

    /// <summary>Tag of the speaking notice; a new announcement replaces it rather than adding another.</summary>
    public const string NoticeTag = "speaking";

    /// <summary>Group of the speaking notice.</summary>
    public const string NoticeGroup = "media_player";

    /// <summary>The speaking notice.</summary>
    public static readonly ToastContent Notice = new()
    {
        Title = "Home Assistant is speaking",
        Buttons = [new ToastButton("Stop", StopArguments, OpensUrl: false)],
        Silent = true,

        // On screen for as long as the audio plays: removed when it ends.
        StayOnScreen = true,
        Tag = NoticeTag,
        Group = NoticeGroup,

        // Removed when playback ends; this only matters if the app stops before it can remove it.
        Expiration = TimeSpan.FromMinutes(30),
    };

    private readonly IHttpDownloader _downloader;
    private readonly IAnnouncementPlayer _player;
    private readonly IAudioDucker _ducker;
    private readonly IMediaSessionSource _session;
    private readonly TimeProvider _time;
    private readonly IToastPresenter _toasts;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _current;
    private long _generation;
    private Interruption _interruption;
    private bool _noticeShown;

    /// <summary>Creates the announcer.</summary>
    public MediaAnnouncer(IHttpDownloader downloader, IAnnouncementPlayer player, IAudioDucker ducker, IMediaSessionSource session, IToastPresenter toasts, TimeProvider time)
    {
        _toasts = toasts;
        _downloader = downloader;
        _player = player;
        _ducker = ducker;
        _session = session;
        _time = time;
    }

    private enum Interruption
    {
        None,
        Ducked,
        Paused,
        NothingToPause,
    }

    /// <summary>Whether other audio is currently interrupted. For tests.</summary>
    internal bool IsInterrupted => _interruption != Interruption.None;

    /// <summary>
    /// The URL in an add-on <c>play_media</c> payload (<c>{"media_type": …, "media_id": …}</c>), or null when the
    /// payload isn't that or the URL isn't http(s). Never a local path (spec 12).
    /// </summary>
    public static Uri? ParseRequest(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("media_id", out var id)
                && id.ValueKind == JsonValueKind.String
                && Uri.TryCreate(id.GetString(), UriKind.Absolute, out var url)
                && LimitedHttpDownloader.IsAllowedUrl(url))
            {
                return url;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>
    /// Plays <paramref name="payload"/>'s URL and returns after the interruption has been undone (or a newer
    /// announcement took over). Failures are logged, never thrown.
    /// </summary>
    public async Task PlayAsync(string payload, MediaPlayerSettings settings, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        if (ParseRequest(payload) is not { } url)
        {
            LogBadRequest(logger);
            return;
        }

        var generation = Interlocked.Increment(ref _generation);
        var mine = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancelSafely(Interlocked.Exchange(ref _current, mine));
        var host = url.Host;

        try
        {
            DownloadedFile file;
            try
            {
                var limits = new DownloadLimits(settings.MaxDownloadBytes, TimeSpan.FromSeconds(settings.DownloadTimeoutSeconds), AcceptedTypePrefix);
                file = await _downloader.DownloadAsync(url, limits, mine.Token).ConfigureAwait(false);
            }
            catch (DownloadRejectedException ex)
            {
                // The URL's path and query carry HA's signature, so only the host is logged.
                LogDownloadFailed(logger, host, ex.Message);
                return;
            }

            await _gate.WaitAsync(mine.Token).ConfigureAwait(false);
            try
            {
                if (_interruption == Interruption.None)
                {
                    await BeginInterruptionAsync(settings, logger, mine.Token).ConfigureAwait(false);
                }

                if (settings.ShowNotification && !_noticeShown)
                {
                    await ShowNoticeAsync(logger, mine.Token).ConfigureAwait(false);
                }

                LogPlaying(logger, host, file.Content.Length);
                await _player.PlayAsync(file.Content, file.ContentType, mine.Token).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (mine.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return; // Replaced by a newer announcement, which owns the interruption now.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPlayFailed(logger, ex, host);
        }
        finally
        {
            Interlocked.CompareExchange(ref _current, null, mine);
            mine.Dispose();
        }

        await Task.Delay(RestoreDelay, _time, cancellationToken).ConfigureAwait(false);
        if (Interlocked.Read(ref _generation) == generation)
        {
            await EndInterruptionAsync(generation, logger, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Stops any announcement and undoes the interruption now (module stop, sleep).</summary>
    public async Task StopAsync(ILogger logger, CancellationToken cancellationToken)
    {
        var generation = Interlocked.Increment(ref _generation);
        CancelSafely(Interlocked.Exchange(ref _current, null));
        await EndInterruptionAsync(generation, logger, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CancelSafely(Interlocked.Exchange(ref _current, null));
        _gate.Dispose();
    }

    private static void CancelSafely(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Its announcement already finished.
        }
    }

    private async Task ShowNoticeAsync(ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await _toasts.ShowAsync(Notice, cancellationToken).ConfigureAwait(false);
            _noticeShown = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNoticeFailed(logger, ex);
        }
    }

    private async Task RemoveNoticeAsync(ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await _toasts.RemoveAsync(NoticeTag, NoticeGroup, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNoticeFailed(logger, ex);
        }
    }

    private async Task BeginInterruptionAsync(MediaPlayerSettings settings, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            if (settings.Interruption == MediaPlayerSettings.InterruptionPause)
            {
                var current = await _session.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
                _interruption = current?.Status == MediaPlaybackStatus.Playing
                    && await _session.SendAsync(MediaSessionCommand.Pause, cancellationToken).ConfigureAwait(false)
                    ? Interruption.Paused
                    : Interruption.NothingToPause;
            }
            else
            {
                _interruption = Interruption.Ducked;
                await _ducker.DuckAsync(settings.DuckLevelPercent / 100.0, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Play anyway: an announcement over loud music beats none.
            LogInterruptFailed(logger, ex);
        }
    }

    private async Task EndInterruptionAsync(long generation, ILogger logger, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Interlocked.Read(ref _generation) != generation)
            {
                return;
            }

            if (_noticeShown)
            {
                _noticeShown = false;
                await RemoveNoticeAsync(logger, cancellationToken).ConfigureAwait(false);
            }

            var interruption = _interruption;
            _interruption = Interruption.None;
            switch (interruption)
            {
                case Interruption.Ducked:
                    await _ducker.RestoreAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case Interruption.Paused:
                    await _session.SendAsync(MediaSessionCommand.Play, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRestoreFailed(logger, ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored play_media: expected JSON with an http or https media_id")]
    private static partial void LogBadRequest(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not download the audio from {Host}: {Reason}")]
    private static partial void LogDownloadFailed(ILogger logger, string host, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Playing audio from {Host} ({Bytes} bytes)")]
    private static partial void LogPlaying(ILogger logger, string host, int bytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not play the audio from {Host}")]
    private static partial void LogPlayFailed(ILogger logger, Exception exception, string host);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not lower or pause other audio; playing anyway")]
    private static partial void LogInterruptFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not show or remove the speaking notification")]
    private static partial void LogNoticeFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not bring other audio back")]
    private static partial void LogRestoreFailed(ILogger logger, Exception exception);
}
