using Microsoft.Extensions.Hosting;
using SmartHomeRelay.App.Settings;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
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
    private readonly SettingsWindowOpener _settingsOpener;
    private readonly Func<SettingsForm> _createSettingsForm;
    private readonly ISettingsState _settings;
    private readonly RemovalRequest _removal;
    private readonly string _version;
    private readonly SynchronizationContext _uiContext;
    private readonly CancellationTokenRegistration _stoppingRegistration;
    private readonly Dictionary<StatusSeverity, Icon> _icons;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly NotifyIcon _notifyIcon;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext(
        IRelayStatusSource status,
        IReconnectRequester reconnect,
        ISingleInstanceGuard singleInstance,
        IShellLauncher shell,
        IAppPaths paths,
        IHostApplicationLifetime lifetime,
        TrayNotifier notifier,
        SettingsWindowOpener settingsOpener,
        Func<SettingsForm> createSettingsForm,
        ISettingsState settings,
        RemovalRequest removal,
        IStartupRegistration startup)
    {
        _version = RelayVersion.Describe(startup.IsInstalled);
        _settings = settings;
        _removal = removal;
        _settingsOpener = settingsOpener;
        _createSettingsForm = createSettingsForm;
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
        _menu.Items.Add($"Version {_version}", null, (_, _) => CopyVersion()).ToolTipText = "Click to copy";
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        _menu.Items.Add("Reconnect", null, (_, _) => _reconnect.RequestReconnect());
        _menu.Items.Add("Open logs folder", null, (_, _) => _shell.OpenFolder(_paths.LogsDirectory));
        _menu.Items.Add("Remove from Home Assistant…", null, (_, _) => ConfirmRemoval());
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
        _settingsOpener.Attach(() => _uiContext.Post(_ => OpenSettings(), null));
        _notifyIcon.DoubleClick += (_, _) => OpenSettings();
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
            _settingsOpener.Detach();
            _settingsForm?.Dispose();
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

    /// <summary>Shows the settings window, or brings the open one to the front. UI thread only.</summary>
    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false } open)
        {
            if (open.WindowState == FormWindowState.Minimized)
            {
                open.WindowState = FormWindowState.Normal;
            }

            open.Activate();
            return;
        }

        var form = _createSettingsForm();
        form.FormClosed += (_, _) =>
        {
            form.Dispose();
            _settingsForm = null;
        };
        _settingsForm = form;
        form.Show();
        form.Activate();
    }

    private void CopyVersion()
    {
        Clipboard.SetText(_version);
        ShowBalloon(AppName, $"Version copied: {_version}");
    }

    /// <summary>Spec 4.4: asks, then exits; Program removes the PC once the connection is closed. UI thread only.</summary>
    private void ConfirmRemoval()
    {
        var id = _settings.Current.Settings.Device.Id;
        var answer = MessageBox.Show(
            $"Remove {id} from Home Assistant?\n\n"
            + "Its device and entities are deleted there, including names, icons and areas you set. "
            + $"{AppName} then closes.\n\n"
            + $"It adds the PC again the next time it starts. To keep it removed, turn off Start with Windows or uninstall {AppName}.",
            AppName,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.OK)
        {
            _removal.Requested = true;
            _lifetime.StopApplication();
        }
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
        // Windows cuts tray tooltips at 127 characters (WinForms throws beyond that).
        var tooltip = $"{AppName} {_version}\n{text}";
        _notifyIcon.Text = tooltip.Length <= 127 ? tooltip : tooltip[..126] + "…";
        _notifyIcon.Icon = _icons[RelayStatusDescriber.Severity(status)];
    }
}
