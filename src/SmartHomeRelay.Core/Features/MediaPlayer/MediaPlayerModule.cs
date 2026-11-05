using System.Globalization;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;

namespace SmartHomeRelay.Core.Features.MediaPlayer;

/// <summary>
/// The PC as a Home Assistant <c>media_player</c> through the MQTT Media Player add-on (spec 5.9).
/// <list type="bullet">
/// <item>Mirrors the Windows media session; transport commands and seek go to it.</item>
/// <item>Volume and mute are the default output's, the same as the Audio feature's.</item>
/// <item><c>play_media</c> plays HA's audio over the music (<see cref="MediaAnnouncer"/>).</item>
/// </list>
/// </summary>
public sealed partial class MediaPlayerModule : IFeatureModule, IHasCustomComponents, IPowerAware, IDisposable
{
    /// <summary>How long change notifications are gathered before one refresh.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    /// <summary>How often the state is re-read without a notification.</summary>
    public static readonly TimeSpan SafetyRefreshInterval = TimeSpan.FromSeconds(60);

    /// <summary>A position this far from where it should be counts as a seek and is published at once.</summary>
    public static readonly TimeSpan SeekTolerance = TimeSpan.FromSeconds(3);

    private readonly ISettingsState _settings;
    private readonly IMediaSessionSource _session;
    private readonly IAudioController _audio;
    private readonly IAudioDucker _ducker;
    private readonly IToastPresenter _toasts;
    private readonly MediaAnnouncer _announcer;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<CustomComponent> _components;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dictionary<string, string> _published = new(StringComparer.Ordinal);
    private IModuleContext? _context;
    private CancellationTokenSource? _running;
    private Task? _loops;
    private int _refreshQueued;
    private int _refreshWorker;
    private string _status = Idle;
    private (TimeSpan Position, DateTimeOffset At)? _lastPosition;

    private const string Idle = "idle";

    /// <summary>Creates the module.</summary>
    public MediaPlayerModule(
        ISettingsState settings,
        IMediaSessionSource session,
        IAudioController audio,
        IAnnouncementPlayer player,
        IAudioDucker ducker,
        IHttpDownloader downloader,
        IToastPresenter toasts,
        TimeProvider time)
    {
        _toasts = toasts;
        _settings = settings;
        _session = session;
        _audio = audio;
        _ducker = ducker;
        _time = time;
        _announcer = new MediaAnnouncer(downloader, player, ducker, session, toasts, time);
        _components =
        [
            new CustomComponent(MediaPlayerKeys.Component, MediaPlayerKeys.DiscoveryTopic, MediaPlayerConfig.Build)
            {
                States = MediaPlayerKeys.States,
                Commands = MediaPlayerKeys.Commands,
                CommandHandler = HandleCommandAsync,

                // The add-on keeps its entity when the config is cleared, so show it as off with no stale track.
                DisabledStates = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [MediaPlayerKeys.State] = "off",
                    [MediaPlayerKeys.Title] = string.Empty,
                    [MediaPlayerKeys.Artist] = string.Empty,
                    [MediaPlayerKeys.Album] = string.Empty,
                    [MediaPlayerKeys.Duration] = string.Empty,
                    [MediaPlayerKeys.Position] = string.Empty,
                },
            },
        ];
    }

    /// <inheritdoc />
    public string Id => "media_player";

    /// <inheritdoc />
    public bool IsEnabled(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Features.MediaPlayer.Enabled;
    }

    /// <inheritdoc />
    public IReadOnlyList<EntityDescriptor> DescribeEntities() => [];

    /// <inheritdoc />
    public IReadOnlyList<CustomComponent> DescribeCustomComponents() => _components;

    /// <inheritdoc />
    public async Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _published.Clear();
        _lastPosition = null;
        _status = Idle;

        var prefix = _settings.Current.Settings.Mqtt.DiscoveryPrefix;
        if (prefix != MediaPlayerKeys.AddOnDiscoveryPrefix)
        {
            LogPrefixMismatch(context.Logger, prefix);
        }

        await RecoverDuckAsync(cancellationToken).ConfigureAwait(false);
        await RefreshAsync(forcePosition: true, cancellationToken).ConfigureAwait(false);

        var running = new CancellationTokenSource();
        _running = running;
        _session.Changed += OnChanged;
        _audio.Changed += OnChanged;
        _toasts.Activated += OnToastActivated;
        _loops = Task.WhenAll(SafetyLoopAsync(running.Token), PositionLoopAsync(running.Token));
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _session.Changed -= OnChanged;
        _audio.Changed -= OnChanged;
        _toasts.Activated -= OnToastActivated;
        var running = _running;
        var loops = _loops;
        _running = null;
        _loops = null;
        if (running is not null)
        {
            await running.CancelAsync().ConfigureAwait(false);
            if (loops is not null)
            {
                await loops.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            running.Dispose();
        }

        if (_context is { } context)
        {
            await _announcer.StopAsync(context.Logger, cancellationToken).ConfigureAwait(false);
        }

        _context = null;
    }

    /// <inheritdoc />
    public async Task OnSuspendingAsync(CancellationToken cancellationToken)
    {
        if (_context is { } context)
        {
            await _announcer.StopAsync(context.Logger, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task OnResumedAsync(CancellationToken cancellationToken)
    {
        // If the restore didn't make it before sleep, the saved levels come back now.
        await RecoverDuckAsync(cancellationToken).ConfigureAwait(false);
        RequestRefresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _session.Changed -= OnChanged;
        _audio.Changed -= OnChanged;
        _toasts.Activated -= OnToastActivated;
        _running?.Cancel();
        _running?.Dispose();
        _announcer.Dispose();
        _refreshGate.Dispose();
    }

    /// <summary>Reads the session and volume and publishes what changed. Internal for tests.</summary>
    internal async Task RefreshAsync(bool forcePosition, CancellationToken cancellationToken)
    {
        if (_context is not { } context)
        {
            return;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MediaSessionInfo? session = null;
            try
            {
                session = await _session.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogReadFailed(context.Logger, ex);
            }

            AudioState? audio = null;
            try
            {
                audio = await _audio.GetStateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogReadFailed(context.Logger, ex);
            }

            var status = MapStatus(session?.Status, _status);
            var trackChanged = false;
            trackChanged |= await PublishIfChangedAsync(context, MediaPlayerKeys.State, status, cancellationToken).ConfigureAwait(false);
            trackChanged |= await PublishIfChangedAsync(context, MediaPlayerKeys.Title, session?.Title ?? string.Empty, cancellationToken).ConfigureAwait(false);
            await PublishIfChangedAsync(context, MediaPlayerKeys.Artist, session?.Artist ?? string.Empty, cancellationToken).ConfigureAwait(false);
            await PublishIfChangedAsync(context, MediaPlayerKeys.Album, session?.Album ?? string.Empty, cancellationToken).ConfigureAwait(false);
            await PublishIfChangedAsync(context, MediaPlayerKeys.Duration, Seconds(session?.Duration), cancellationToken).ConfigureAwait(false);
            await PublishIfChangedAsync(context, MediaPlayerKeys.MediaType, session?.MediaType ?? string.Empty, cancellationToken).ConfigureAwait(false);
            _status = status;

            if (forcePosition || trackChanged || Jumped(session?.Position, status))
            {
                await PublishPositionAsync(context, session?.Position, cancellationToken).ConfigureAwait(false);
            }

            // Unknown volume or mute is left out: the add-on reads an empty volume as 0.
            if (audio?.Volume is { } volume)
            {
                await PublishIfChangedAsync(context, MediaPlayerKeys.Volume, FormatVolume(volume), cancellationToken).ConfigureAwait(false);
            }

            if (audio?.Muted is { } muted)
            {
                await PublishIfChangedAsync(context, MediaPlayerKeys.Mute, muted ? "mute" : "unmute", cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>The add-on's state for a Windows status; <c>Changing</c> keeps the previous one.</summary>
    internal static string MapStatus(MediaPlaybackStatus? status, string previous) => status switch
    {
        MediaPlaybackStatus.Playing => "playing",
        MediaPlaybackStatus.Paused => "paused",
        MediaPlaybackStatus.Changing => previous,
        _ => Idle,
    };

    /// <summary>Volume as the add-on reads it, e.g. <c>0.40</c>.</summary>
    internal static string FormatVolume(double level) => Math.Clamp(level, 0, 1).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Whole seconds, or empty (HA's unknown) when not known.</summary>
    internal static string Seconds(TimeSpan? value) =>
        value is { } v && v >= TimeSpan.Zero ? ((long)v.TotalSeconds).ToString(CultureInfo.InvariantCulture) : string.Empty;

    private bool Jumped(TimeSpan? position, string status)
    {
        if (position is not { } now || _lastPosition is not { } last)
        {
            return position.HasValue != _lastPosition.HasValue;
        }

        var expected = status == "playing" ? last.Position + (_time.GetUtcNow() - last.At) : last.Position;
        return (now - expected).Duration() > SeekTolerance;
    }

    private async Task PublishPositionAsync(IModuleContext context, TimeSpan? position, CancellationToken cancellationToken)
    {
        await context.PublishCustomStateAsync(MediaPlayerKeys.Component, MediaPlayerKeys.Position, Seconds(position), cancellationToken).ConfigureAwait(false);
        _lastPosition = position is { } p ? (p, _time.GetUtcNow()) : null;
    }

    private async Task<bool> PublishIfChangedAsync(IModuleContext context, string state, string payload, CancellationToken cancellationToken)
    {
        if (_published.TryGetValue(state, out var last) && last == payload)
        {
            return false;
        }

        await context.PublishCustomStateAsync(MediaPlayerKeys.Component, state, payload, cancellationToken).ConfigureAwait(false);
        _published[state] = payload;
        return true;
    }

    private async Task HandleCommandAsync(string command, string payload, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case MediaPlayerKeys.Play:
                await SendAsync(MediaSessionCommand.Play, cancellationToken).ConfigureAwait(false);
                break;
            case MediaPlayerKeys.Pause:
                await SendAsync(MediaSessionCommand.Pause, cancellationToken).ConfigureAwait(false);
                break;
            case MediaPlayerKeys.PlayPause:
                await SendAsync(MediaSessionCommand.TogglePlayPause, cancellationToken).ConfigureAwait(false);
                break;
            case MediaPlayerKeys.Next:
                await SendAsync(MediaSessionCommand.Next, cancellationToken).ConfigureAwait(false);
                break;
            case MediaPlayerKeys.Previous:
                await SendAsync(MediaSessionCommand.Previous, cancellationToken).ConfigureAwait(false);
                break;
            case MediaPlayerKeys.Volume when TryParseNumber(payload, out var level):
                await _audio.SetVolumeAsync(Math.Clamp(level, 0, 1), cancellationToken).ConfigureAwait(false);
                RequestRefresh();
                break;
            case MediaPlayerKeys.Mute when payload.Trim().ToLowerInvariant() is "mute" or "unmute":
                await _audio.SetMutedAsync(payload.Trim().Equals("mute", StringComparison.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false);
                RequestRefresh();
                break;
            case MediaPlayerKeys.Seek when TryParseNumber(payload, out var seconds) && seconds >= 0:
                if (!await _session.SeekAsync(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false))
                {
                    Log(l => LogRefused(l, command));
                }

                RequestRefresh();
                break;
            case MediaPlayerKeys.PlayMedia:
                if (_context is { } context)
                {
                    await _announcer.PlayAsync(payload, _settings.Current.Settings.Features.MediaPlayer, context.Logger, cancellationToken).ConfigureAwait(false);
                }

                break;
            default:
                Log(l => LogBadPayload(l, command));
                break;
        }
    }

    private static bool TryParseNumber(string payload, out double value) =>
        double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private async Task SendAsync(MediaSessionCommand command, CancellationToken cancellationToken)
    {
        if (!await _session.SendAsync(command, cancellationToken).ConfigureAwait(false))
        {
            Log(l => LogRefused(l, command.ToString()));
        }

        RequestRefresh();
    }

    private async Task RecoverDuckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _ducker.RecoverAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log(l => LogRecoverFailed(l, ex));
        }
    }

    private void OnChanged(object? sender, EventArgs e) => RequestRefresh();

    /// <summary>The speaking notice's Stop button: stop the announcement and bring the other audio back.</summary>
    private void OnToastActivated(object? sender, string arguments)
    {
        if (arguments != MediaAnnouncer.StopArguments || _context is not { } context)
        {
            return;
        }

        LogStopFromNotice(context.Logger);
        _ = Task.Run(async () =>
        {
            try
            {
                await _announcer.StopAsync(context.Logger, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not ObjectDisposedException)
            {
                Log(l => LogReadFailed(l, ex));
            }
        });
    }

    private void RequestRefresh()
    {
        Interlocked.Exchange(ref _refreshQueued, 1);
        if (Interlocked.CompareExchange(ref _refreshWorker, 1, 0) == 0)
        {
            _ = Task.Run(DrainRefreshesAsync, CancellationToken.None);
        }
    }

    private async Task DrainRefreshesAsync()
    {
        while (true)
        {
            try
            {
                while (Interlocked.Exchange(ref _refreshQueued, 0) == 1)
                {
                    var token = _running?.Token ?? CancellationToken.None;
                    await Task.Delay(Debounce, _time, token).ConfigureAwait(false);
                    await RefreshAsync(forcePosition: false, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log(l => LogReadFailed(l, ex));
            }

            Interlocked.Exchange(ref _refreshWorker, 0);
            if (Volatile.Read(ref _refreshQueued) == 0 || Interlocked.CompareExchange(ref _refreshWorker, 1, 0) != 0)
            {
                return;
            }
        }
    }

    private async Task SafetyLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(SafetyRefreshInterval, _time, cancellationToken).ConfigureAwait(false);
                RequestRefresh();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Publishes the position every <c>positionIntervalSeconds</c> while playing (spec 5.9).</summary>
    private async Task PositionLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var seconds = Math.Max(1, _settings.Current.Settings.Features.MediaPlayer.PositionIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(seconds), _time, cancellationToken).ConfigureAwait(false);
                if (_status == "playing")
                {
                    await RefreshAsync(forcePosition: true, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log(l => LogReadFailed(l, ex));
        }
    }

    private void Log(Action<ILogger> write)
    {
        if (_context?.Logger is { } logger)
        {
            write(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "mqtt.discoveryPrefix is '{Prefix}', but the MQTT Media Player add-on only listens on 'homeassistant'; the media player is published there")]
    private static partial void LogPrefixMismatch(ILogger logger, string prefix);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the media session or volume; will retry")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored media player command {Command}: unexpected payload")]
    private static partial void LogBadPayload(ILogger logger, string command);

    [LoggerMessage(Level = LogLevel.Information, Message = "Media player command {Command}: no media session, or it refused")]
    private static partial void LogRefused(ILogger logger, string command);

    [LoggerMessage(Level = LogLevel.Information, Message = "Announcement stopped from the notification")]
    private static partial void LogStopFromNotice(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not restore volumes left lowered by an earlier announcement")]
    private static partial void LogRecoverFailed(ILogger logger, Exception exception);
}
