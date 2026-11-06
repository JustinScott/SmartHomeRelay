using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Toasts;
using Windows.UI.Notifications;
using XmlDocument = Windows.Data.Xml.Dom.XmlDocument;

namespace SmartHomeRelay.Platform.Windows.Notifications;

/// <summary>
/// Shows toasts with Windows' built-in <c>Windows.UI.Notifications</c> (spec 5.6; no package). The XML comes from
/// <see cref="ToastXmlBuilder"/>. URL clicks are protocol activations Windows handles itself; clicks on app buttons
/// raise <see cref="Activated"/> while the app runs (there is no COM activator, so not from the notification
/// center once the app has exited).
/// </summary>
public sealed partial class WindowsToastPresenter : IToastPresenter
{
    private readonly ToastAppIdentity _identity;
    private readonly ILogger<WindowsToastPresenter> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Tag, string Group), ToastNotification> _tagged = [];
    private ToastNotifier? _notifier;

    /// <summary>Creates the presenter; the app is registered on the first toast.</summary>
    public WindowsToastPresenter(ToastAppIdentity identity, ILogger<WindowsToastPresenter> logger)
    {
        _identity = identity;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<string>? Activated;

    /// <inheritdoc />
    public Task ShowAsync(ToastContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var xml = new XmlDocument();
        xml.LoadXml(ToastXmlBuilder.Build(content));

        var toast = new ToastNotification(xml) { ExpirationTime = DateTimeOffset.Now + content.Expiration };
        if (content.Tag is { } tag)
        {
            toast.Tag = tag;
            toast.Group = content.Group ?? string.Empty;
        }

        toast.Activated += OnActivated;
        toast.Failed += OnFailed;

        lock (_gate)
        {
            if (content.Tag is { } key)
            {
                _tagged[(key, content.Group ?? string.Empty)] = toast;
            }

            Notifier().Show(toast);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string tag, string group, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_tagged.Remove((tag, group), out var toast))
            {
                try
                {
                    Notifier().Hide(toast);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    // Already gone from the screen.
                }
            }

            // Also clears it from the notification center, so it leaves no history.
            ToastNotificationManager.History.Remove(tag, group, _identity.AppUserModelId);
        }

        return Task.CompletedTask;
    }

    private ToastNotifier Notifier()
    {
        if (_notifier is null)
        {
            ToastAppRegistration.Register(_identity);
            _notifier = ToastNotificationManager.CreateToastNotifier(_identity.AppUserModelId);
            LogIfNotificationsOff(_notifier);
        }

        return _notifier;
    }

    /// <summary>
    /// Diagnostics only. For an app registered without a Start Menu shortcut, Windows can't answer and throws
    /// <c>E_NOTFOUND</c> (seen on BOX1); that must never stop the toast from being shown.
    /// </summary>
    private void LogIfNotificationsOff(ToastNotifier notifier)
    {
        try
        {
            if (notifier.Setting != NotificationSetting.Enabled)
            {
                LogNotificationsOff(_logger, notifier.Setting.ToString());
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
    }

    private void OnActivated(ToastNotification sender, object args)
    {
        if (args is ToastActivatedEventArgs { Arguments: { Length: > 0 } arguments } && !arguments.Contains("://", StringComparison.Ordinal))
        {
            // Never raised inline: Windows' notification thread must not be blocked.
            ThreadPool.QueueUserWorkItem(state => Activated?.Invoke(this, state), arguments, preferLocal: false);
        }
    }

    private void OnFailed(ToastNotification sender, ToastFailedEventArgs args) =>
        LogFailed(_logger, args.ErrorCode.HResult);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows notifications for Smart Home Relay are not enabled ({Setting}); toasts won't appear. Check Settings > System > Notifications.")]
    private static partial void LogNotificationsOff(ILogger logger, string setting);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows could not show a toast (0x{ErrorCode:X8})")]
    private static partial void LogFailed(ILogger logger, int errorCode);
}
