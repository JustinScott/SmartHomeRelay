using Microsoft.Extensions.Hosting;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Hosting;

namespace SmartHomeRelay.App.Tray;

/// <summary>
/// The tray icon and its menu (spec 7.1). A thin view: status comes from <see cref="RelayStatusDescriber"/>.
/// Menu items are added in the milestones that implement them.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppName = "Smart Home Relay";
    private const int BalloonTimeoutMs = 3000;

    private readonly ISettingsState _settings;
    private readonly ISingleInstanceGuard _singleInstance;
    private readonly IFolderOpener _folderOpener;
    private readonly IAppPaths _paths;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly SynchronizationContext _uiContext;
    private readonly CancellationTokenRegistration _stoppingRegistration;
    private readonly Icon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly NotifyIcon _notifyIcon;

    public TrayApplicationContext(
        ISettingsState settings,
        ISingleInstanceGuard singleInstance,
        IFolderOpener folderOpener,
        IAppPaths paths,
        IHostApplicationLifetime lifetime)
    {
        _settings = settings;
        _singleInstance = singleInstance;
        _folderOpener = folderOpener;
        _paths = paths;
        _lifetime = lifetime;

        // Creating the first control installs the WinForms synchronization context on this (UI) thread.
        _menu = new ContextMenuStrip();
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("The tray must be created on the UI thread.");

        _statusItem = new ToolStripMenuItem { Enabled = false };
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Open logs folder", null, (_, _) => _folderOpener.Open(_paths.LogsDirectory));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => _lifetime.StopApplication());

        _icon = TrayIcons.LoadAppIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            ContextMenuStrip = _menu,
            Visible = true,
        };

        UpdateStatus();
        _settings.Changed += OnSettingsChanged;
        _singleInstance.SecondInstanceStarted += OnSecondInstanceStarted;
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
            _settings.Changed -= OnSettingsChanged;
            _singleInstance.SecondInstanceStarted -= OnSecondInstanceStarted;
            _stoppingRegistration.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => _uiContext.Post(_ => UpdateStatus(), null);

    private void OnSecondInstanceStarted(object? sender, EventArgs e) =>
        _uiContext.Post(
            _ => _notifyIcon.ShowBalloonTip(BalloonTimeoutMs, AppName, $"{AppName} is already running.", ToolTipIcon.Info),
            null);

    private void UpdateStatus()
    {
        var text = RelayStatusDescriber.Describe(RelayStatusDescriber.FromSettings(_settings.Current));
        _statusItem.Text = $"Status: {text}";
        _notifyIcon.Text = $"{AppName}: {text}";
    }
}
