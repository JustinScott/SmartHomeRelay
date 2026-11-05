using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Net;
using SmartHomeRelay.Core.Toasts;

namespace SmartHomeRelay.Core.Features.Notifications;

/// <summary>
/// Windows toasts sent from Home Assistant (spec 5.6), on the prototype's topic
/// <c>{baseTopic}/{deviceId}/commands/notification</c> with its JSON fields. That topic is also the command topic of
/// a notify entity, so HA's <c>notify.send_message</c> works too (its message arrives as plain text).
/// <list type="bullet">
/// <item>Payloads go on a small bounded queue and are shown by one background worker; when full, the oldest waiting one is dropped.</item>
/// <item>A hero image is downloaded (http/https only, size and time limits, image types only) to a local file; on any failure the toast shows without it.</item>
/// </list>
/// </summary>
public sealed partial class NotificationsModule : IFeatureModule, IDisposable
{
    /// <summary>How many notifications may wait while one is being shown.</summary>
    public const int QueueCapacity = 8;

    /// <summary>How long a toast stays in the notification center (as in the prototype).</summary>
    public static readonly TimeSpan ToastExpiration = TimeSpan.FromDays(1);

    /// <summary>How often old images are swept.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    private readonly ISettingsState _settings;
    private readonly IToastPresenter _toasts;
    private readonly IHttpDownloader _downloader;
    private readonly HeroImageStore _images;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<EntityDescriptor> _entities;
    private Channel<string>? _queue;
    private CancellationTokenSource? _running;
    private Task? _loops;
    private IModuleContext? _context;

    /// <summary>Creates the module.</summary>
    public NotificationsModule(ISettingsState settings, IToastPresenter toasts, IHttpDownloader downloader, IAppPaths paths, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _settings = settings;
        _toasts = toasts;
        _downloader = downloader;
        _time = time;
        _images = new HeroImageStore(Path.Combine(paths.CacheDirectory, "notification-images"), time);
        _entities =
        [
            new(NotificationsKeys.Notification, "Notification", EntityComponent.Notify)
            {
                Icon = "mdi:message-badge",
                CommandTopic = topics => topics.NotificationCommand,
                CommandHandler = EnqueueAsync,
            },
        ];
    }

    /// <inheritdoc />
    public string Id => "notifications";

    /// <summary>Where downloaded images are kept. For tests.</summary>
    internal string ImageDirectory => _images.Directory;

    /// <inheritdoc />
    public bool IsEnabled(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Features.Notifications.Enabled;
    }

    /// <inheritdoc />
    public IReadOnlyList<EntityDescriptor> DescribeEntities() => _entities;

    /// <inheritdoc />
    public Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        SweepImages();

        var queue = Channel.CreateBounded<string>(
            new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => Log(LogDropped));
        _queue = queue;
        var running = new CancellationTokenSource();
        _running = running;
        _loops = Task.WhenAll(ShowLoopAsync(queue.Reader, running.Token), SweepLoopAsync(running.Token));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue?.Writer.TryComplete();
        var running = _running;
        var loops = _loops;
        _running = null;
        _loops = null;
        _queue = null;
        if (running is not null)
        {
            await running.CancelAsync().ConfigureAwait(false);
            if (loops is not null)
            {
                await loops.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            running.Dispose();
        }

        _context = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _queue?.Writer.TryComplete();
        _running?.Cancel();
        _running?.Dispose();
    }

    /// <summary>Parses, downloads the image, and shows one notification. Never throws. Internal for tests.</summary>
    internal async Task ShowAsync(string payload, CancellationToken cancellationToken)
    {
        var parsed = NotificationParser.Parse(payload);
        foreach (var problem in parsed.Problems)
        {
            Log(l => LogProblem(l, problem));
        }

        if (parsed.HadEventId)
        {
            Log(LogEventIdIgnored);
        }

        if (parsed.Request is not { } request)
        {
            return;
        }

        try
        {
            var content = new ToastContent
            {
                Title = request.Title,
                Message = request.Message,
                LaunchUrl = request.ActivationUrl,
                Buttons = [.. request.Buttons.Select(b => new ToastButton(b.Label, b.Url.AbsoluteUri, OpensUrl: true))],
                Expiration = ToastExpiration,
                HeroImagePath = request.HeroImageUrl is { } url ? await DownloadImageAsync(url, cancellationToken).ConfigureAwait(false) : null,
            };

            await _toasts.ShowAsync(content, cancellationToken).ConfigureAwait(false);
            Log(l => LogShown(l, content.HeroImagePath is not null, content.Buttons.Count));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log(l => LogShowFailed(l, ex));
        }
    }

    private Task EnqueueAsync(string payload, CancellationToken cancellationToken)
    {
        // Never wait here: the drop-oldest queue always accepts, so a slow download can't hold up other commands.
        if (_queue is not { } queue || !queue.Writer.TryWrite(payload))
        {
            Log(LogNotRunning);
        }

        return Task.CompletedTask;
    }

    private async Task<string?> DownloadImageAsync(Uri url, CancellationToken cancellationToken)
    {
        var options = _settings.Current.Settings.Features.Notifications;
        try
        {
            var limits = new DownloadLimits(options.ImageMaxBytes, TimeSpan.FromSeconds(options.ImageDownloadTimeoutSeconds), "image/");
            var file = await _downloader.DownloadAsync(url, limits, cancellationToken).ConfigureAwait(false);
            if (!HeroImageStore.IsSupported(file.ContentType))
            {
                Log(l => LogImageType(l, url.Host, file.ContentType));
                return null;
            }

            return await _images.SaveAsync(file.Content, file.ContentType, cancellationToken).ConfigureAwait(false);
        }
        catch (DownloadRejectedException ex)
        {
            // Only the host: image URLs from HA can carry tokens.
            Log(l => LogImageFailed(l, url.Host, ex.Message));
            return null;
        }
        catch (IOException ex)
        {
            Log(l => LogImageFailed(l, url.Host, ex.Message));
            return null;
        }
    }

    private async Task ShowLoopAsync(ChannelReader<string> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var payload in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await ShowAsync(payload, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SweepLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(SweepInterval, _time, cancellationToken).ConfigureAwait(false);
                SweepImages();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Images live as long as their toast can still be opened from the notification center.</summary>
    private void SweepImages()
    {
        try
        {
            _images.Sweep(ToastExpiration + TimeSpan.FromHours(1));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(l => LogSweepFailed(l, ex));
        }
    }

    private void Log(Action<ILogger> write)
    {
        if (_context?.Logger is { } logger)
        {
            write(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification: {Problem}")]
    private static partial void LogProblem(ILogger logger, string problem);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Notification: EventID is no longer supported and was ignored; send HeroImageURL and ActivationURL instead")]
    private static partial void LogEventIdIgnored(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification shown (image: {HasImage}, buttons: {Buttons})")]
    private static partial void LogShown(ILogger logger, bool hasImage, int buttons);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not show the notification")]
    private static partial void LogShowFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification image from {Host} not shown: {Reason}")]
    private static partial void LogImageFailed(ILogger logger, string host, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification image from {Host} not shown: toasts can't show '{ContentType}' (PNG, JPEG and GIF only)")]
    private static partial void LogImageType(ILogger logger, string host, string contentType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Too many notifications at once; the oldest waiting one was dropped")]
    private static partial void LogDropped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification ignored: the feature is stopping")]
    private static partial void LogNotRunning(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not clean up old notification images")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
