using Microsoft.Extensions.Hosting;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.App.Tray;

/// <summary>
/// The tray icon and its menu (spec 7.1). A thin view: status text and severity come from
/// <see cref="RelayStatusDescriber"/>. Menu items are added in the milestones that implement them.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppName = "Smart Home Relay";
    private const int BalloonTimeoutMs = 5000;

    private readonly IRelayStatusSource _status;
    private readonly IReconnectRequester _reconnect;
    private readonly ISingleInstanceGuard _singleInstance;
    private readonly IShellLauncher _shell;
    private readonly IAppPaths _paths;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TrayNotifier _notifier;
    private readonly SynchronizationContext _uiContext;
    private readonly CancellationTokenRegistration _stoppingRegistration;
    private readonly Dictionary<StatusSeverity, Icon> _icons;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly NotifyIcon _notifyIcon;

    public TrayApplicationContext(
        IRelayStatusSource status,
        IReconnectRequester reconnect,
        ISingleInstanceGuard singleInstance,
        IShellLauncher shell,
        IAppPaths paths,
        IHostApplicationLifetime lifetime,
        TrayNotifier notifier)
    {
        _status = status;
        _reconnect = reconnect;
        _singleInstance = singleInstance;
        _shell = shell;
        _paths = paths;
        _lifetime = lifetime;
        _notifier = notifier;

        // Creating the first control installs the WinForms synchronization context on this (UI) thread.
        _menu = new ContextMenuStrip();
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("The tray must be created on the UI thread.");

        _statusItem = new ToolStripMenuItem { Enabled = false };
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());

        // Until the settings window exists (milestone 9), Settings opens settings.json (spec 7).
        _menu.Items.Add("Settings", null, (_, _) => _shell.OpenFile(_paths.SettingsFilePath));
        _menu.Items.Add("Reconnect", null, (_, _) => _reconnect.RequestReconnect());
        _menu.Items.Add("Open logs folder", null, (_, _) => _shell.OpenFolder(_paths.LogsDirectory));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => _lifetime.StopApplication());

        _icons = Enum.GetValues<StatusSeverity>().ToDictionary(s => s, TrayIcons.Load);
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Visible = true,
        };

        UpdateStatus();
        _status.StatusChanged += OnStatusChanged;
        _singleInstance.SecondInstanceStarted += OnSecondInstanceStarted;
        _notifier.Attach((title, message) => _uiContext.Post(_ => ShowBalloon(title, message), null));
        _stoppingRegistration = _lifetime.ApplicationStopping.Register(() => _uiContext.Post(_ => ExitThread(), null));
    }

    protected override void ExitThreadCore()
    {
        // Hide before the message loop ends so no ghost icon is left behind (spec 6).
        _notifyIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifier.Detach();
            _status.StatusChanged -= OnStatusChanged;
            _singleInstance.SecondInstanceStarted -= OnSecondInstanceStarted;
            _stoppingRegistration.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            foreach (var icon in _icons.Values)
            {
                icon.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private void OnStatusChanged(object? sender, EventArgs e) => _uiContext.Post(_ => UpdateStatus(), null);

    private void OnSecondInstanceStarted(object? sender, EventArgs e) =>
        _uiContext.Post(_ => ShowBalloon(AppName, $"{AppName} is already running."), null);

    private void ShowBalloon(string title, string message) =>
        _notifyIcon.ShowBalloonTip(BalloonTimeoutMs, title, message, ToolTipIcon.Info);

    private void UpdateStatus()
    {
        var status = _status.Status;
        var text = RelayStatusDescriber.Describe(status);
        _statusItem.Text = $"Status: {text}";
        _notifyIcon.Text = $"{AppName}: {text}";
        _notifyIcon.Icon = _icons[RelayStatusDescriber.Severity(status)];
    }
}
