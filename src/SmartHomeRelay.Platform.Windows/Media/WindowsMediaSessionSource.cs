using System.Runtime.InteropServices;
using SmartHomeRelay.Core.Abstractions;
using Windows.Foundation;
using MediaPlaybackType = Windows.Media.MediaPlaybackType;
using Windows.Media.Control;

namespace SmartHomeRelay.Platform.Windows.Media;

/// <summary>
/// The session Windows' media controls show, through <c>GlobalSystemMediaTransportControlsSessionManager</c>
/// (spec 5.9; Windows 10 1809+, standard user, no package). Follows the current session as it changes and
/// raises <see cref="Changed"/> on any of its changes.
/// </summary>
public sealed class WindowsMediaSessionSource : IMediaSessionSource, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _watched;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task<MediaSessionInfo?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        if (await CurrentSessionAsync(cancellationToken).ConfigureAwait(false) is not { } session)
        {
            return null;
        }

        try
        {
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            GlobalSystemMediaTransportControlsSessionMediaProperties? media = null;
            try
            {
                media = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken).ConfigureAwait(false);
            }
            catch (COMException)
            {
                // The app is closing its session; report what's left.
            }

            var status = MapStatus(playback.PlaybackStatus);
            var (duration, position) = Timeline(
                timeline.StartTime, timeline.EndTime, timeline.Position, timeline.LastUpdatedTime, status, playback.PlaybackRate, DateTimeOffset.UtcNow);
            return new MediaSessionInfo(
                status,
                Clean(media?.Title),
                Clean(media?.Artist) ?? Clean(media?.AlbumArtist),
                Clean(media?.AlbumTitle),
                duration,
                position,
                MediaType(media?.PlaybackType ?? playback.PlaybackType));
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendAsync(MediaSessionCommand command, CancellationToken cancellationToken)
    {
        if (await CurrentSessionAsync(cancellationToken).ConfigureAwait(false) is not { } session)
        {
            return false;
        }

        IAsyncOperation<bool> operation = command switch
        {
            MediaSessionCommand.Play => session.TryPlayAsync(),
            MediaSessionCommand.Pause => session.TryPauseAsync(),
            MediaSessionCommand.TogglePlayPause => session.TryTogglePlayPauseAsync(),
            MediaSessionCommand.Next => session.TrySkipNextAsync(),
            MediaSessionCommand.Previous => session.TrySkipPreviousAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
        return await operation.AsTask(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        if (await CurrentSessionAsync(cancellationToken).ConfigureAwait(false) is not { } session)
        {
            return false;
        }

        // Positions are relative to the timeline's start.
        var start = session.GetTimelineProperties().StartTime;
        return await session.TryChangePlaybackPositionAsync((start + position).Ticks).AsTask(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            _disposed = true;
            Watch(null);
            if (_manager is { } manager)
            {
                manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                manager.SessionsChanged -= OnSessionsChanged;
            }
        }
        finally
        {
            _gate.Release();
        }

        _gate.Dispose();
    }

    internal static MediaPlaybackStatus MapStatus(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackStatus.Stopped,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackStatus.Changing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackStatus.Opened,
        _ => MediaPlaybackStatus.Closed,
    };

    /// <summary>
    /// Duration and position relative to the timeline's start. Apps update the timeline rarely, so while playing
    /// the position is advanced by the time since the app last reported it.
    /// </summary>
    internal static (TimeSpan? Duration, TimeSpan? Position) Timeline(
        TimeSpan start,
        TimeSpan end,
        TimeSpan reported,
        DateTimeOffset reportedAt,
        MediaPlaybackStatus status,
        double? rate,
        DateTimeOffset now)
    {
        var length = end - start;
        TimeSpan? duration = length > TimeSpan.Zero ? length : null;
        if (duration is null && reported <= TimeSpan.Zero)
        {
            return (null, null);
        }

        var position = reported - start;
        if (status == MediaPlaybackStatus.Playing && reportedAt > DateTimeOffset.MinValue)
        {
            var elapsed = now - reportedAt;
            if (elapsed > TimeSpan.Zero)
            {
                position += elapsed * (rate is > 0 ? rate.Value : 1);
            }
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        if (duration is { } d && position > d)
        {
            position = d;
        }

        return (duration, position);
    }

    private static string? MediaType(MediaPlaybackType? type) => type switch
    {
        MediaPlaybackType.Music => "music",
        MediaPlaybackType.Video => "video",
        MediaPlaybackType.Image => "image",
        _ => null,
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<GlobalSystemMediaTransportControlsSession?> CurrentSessionAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_manager is null)
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken).ConfigureAwait(false);
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                _manager.SessionsChanged += OnSessionsChanged;
            }

            var session = _manager.GetCurrentSession();
            Watch(session);
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Moves the change subscriptions to <paramref name="session"/>.</summary>
    private void Watch(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(session, _watched))
        {
            return;
        }

        if (_watched is { } old)
        {
            old.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            old.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            old.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        _watched = session;
        if (session is not null)
        {
            session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Raise();

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => Raise();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Raise();

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Raise();

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => Raise();

    /// <summary>Never raised inline: Windows' event threads must not be blocked.</summary>
    private void Raise() =>
        ThreadPool.QueueUserWorkItem(static self => self.Changed?.Invoke(self, EventArgs.Empty), this, preferLocal: false);
}
