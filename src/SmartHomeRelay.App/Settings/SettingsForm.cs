using Microsoft.Extensions.Logging;
using SmartHomeRelay.App.Tray;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.App.Settings;

/// <summary>
/// The settings window (spec 7.1): Connection, Device, Features, General. A thin view over
/// <see cref="SettingsEditor"/> and <see cref="MqttConnectionTester"/>; everything it shows comes from and goes
/// back to <see cref="AppSettings"/>. Rare options stay in settings.json (the General tab opens it).
/// </summary>
internal sealed class SettingsForm : Form
{
    private const string PasswordSavedHint = "Saved. Type to replace it.";

    private readonly SettingsEditor _editor;
    private readonly MqttConnectionTester _tester;
    private readonly IShellLauncher _shell;
    private readonly IAppPaths _paths;
    private readonly SettingsDraft _draft;

    // Connection
    private readonly TextBox _host = TextField();
    private readonly NumericUpDown _port = Number(1, 65_535);
    private readonly TextBox _username = TextField();
    private readonly TextBox _password = Password();
    private readonly Button _test = new() { Text = "Test connection", AutoSize = true };
    private readonly Label _testResult = Note();

    // Device
    private readonly TextBox _deviceName = TextField();
    private readonly TextBox _deviceId = TextField();

    // Features
    private readonly CheckBox _presence = Feature("Presence");
    private readonly NumericUpDown _idleSeconds = Number(SettingsValidator.MinIdleThresholdSeconds, SettingsValidator.MaxIdleThresholdSeconds);
    private readonly CheckBox _power = Feature("Power");
    private readonly CheckBox _allowShutdown = Option("Allow shutdown");
    private readonly CheckBox _allowRestart = Option("Allow restart");
    private readonly CheckBox _allowHibernate = Option("Allow hibernate");
    private readonly CheckBox _allowSignOut = Option("Allow sign out");
    private readonly NumericUpDown _shutdownDelay = Number(0, SettingsValidator.MaxShutdownDelaySeconds);
    private readonly CheckBox _display = Feature("Display");
    private readonly CheckBox _audio = Feature("Audio");
    private readonly CheckBox _metrics = Feature("Metrics");
    private readonly NumericUpDown _metricsInterval = Number(SettingsValidator.MinMetricsIntervalSeconds, SettingsValidator.MaxMetricsIntervalSeconds);
    private readonly CheckBox _notifications = Feature("Notifications");
    private readonly CheckBox _mediaPlayer = Feature("Media player (needs the MQTT Media Player add-on)");
    private readonly ComboBox _interruption = Choice("Lower other apps", "Pause what's playing");
    private readonly NumericUpDown _duckLevel = Number(0, 100);
    private readonly CheckBox _speakingNotice = Option("Show a notification while Home Assistant speaks");
    private readonly CheckBox _hardware = Feature("Hardware sensors (needs LibreHardwareMonitor)");
    private readonly NumericUpDown _lhmPort = Number(1, 65_535);
    private readonly TextBox _lhmUsername = TextField();
    private readonly TextBox _lhmPassword = Password();
    private readonly NumericUpDown _lhmInterval = Number(SettingsValidator.MinHardwareIntervalSeconds, SettingsValidator.MaxHardwareIntervalSeconds);

    // General
    private readonly CheckBox _startWithWindows = Option("Start Smart Home Relay when I sign in");
    private readonly ComboBox _logLevel = Choice([.. Enum.GetValues<LogLevel>().Where(l => l != LogLevel.None).Select(l => l.ToString())]);

    private readonly Label _problems = Note();
    private readonly Button _save = new() { Text = "Save", AutoSize = true, MinimumSize = new Size(88, 0) };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, MinimumSize = new Size(88, 0), DialogResult = DialogResult.Cancel };

    public SettingsForm(SettingsEditor editor, MqttConnectionTester tester, IStartupRegistration startup, IShellLauncher shell, IAppPaths paths)
    {
        _editor = editor;
        _tester = tester;
        _shell = shell;
        _paths = paths;
        _draft = editor.Load();

        Text = "Smart Home Relay settings";
        Icon = TrayIcons.Load(StatusSeverity.Ok);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        Size = new Size(560, 640);
        MinimumSize = new Size(480, 480);
        Padding = new Padding(8);
        AcceptButton = _save;
        CancelButton = _cancel;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("Connection", ConnectionTab()));
        tabs.TabPages.Add(Page("Device", DeviceTab()));
        tabs.TabPages.Add(Page("Features", FeaturesTab()));
        tabs.TabPages.Add(Page("General", GeneralTab(startup)));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_save);

        _problems.Dock = DockStyle.Bottom;
        _problems.ForeColor = Color.Firebrick;
        _problems.Padding = new Padding(0, 8, 0, 0);

        Controls.Add(tabs);
        Controls.Add(_problems);
        Controls.Add(buttons);

        if (_draft.FileProblems.Count > 0)
        {
            var banner = Note();
            banner.Dock = DockStyle.Top;
            banner.ForeColor = Color.DarkGoldenrod;
            banner.Padding = new Padding(0, 0, 0, 8);
            banner.Text = "settings.json has problems, so the app is running on defaults. Saving here replaces it:\n"
                + string.Join("\n", _draft.FileProblems.Take(5));
            Controls.Add(banner);
        }

        Populate(_draft.Settings);
        _test.Click += OnTestClicked;
        _save.Click += OnSaveClicked;
        Resize += (_, _) => FitNotes(this);
    }

    private Control ConnectionTab()
    {
        var grid = Grid();
        Row(grid, "Broker address", _host);
        Row(grid, "Port", _port);
        Row(grid, "User name", _username);
        Row(grid, "Password", _password);
        Row(grid, string.Empty, _test);
        Row(grid, string.Empty, _testResult);
        if (_draft.HasMqttPassword)
        {
            _password.PlaceholderText = PasswordSavedHint;
        }

        return grid;
    }

    private Control DeviceTab()
    {
        var grid = Grid();
        Row(grid, "Name in Home Assistant", _deviceName);
        Row(grid, "Device ID", _deviceId);
        var warning = Note();
        warning.Text = "Changing the device ID makes Home Assistant see a new device: entity IDs change and automations that use them stop working.";
        Row(grid, string.Empty, warning);
        return grid;
    }

    private Control FeaturesTab()
    {
        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8),
        };

        list.Controls.Add(Group(_presence, ("Idle after (seconds)", _idleSeconds)));
        list.Controls.Add(Group(
            _power,
            (string.Empty, _allowShutdown),
            (string.Empty, _allowRestart),
            (string.Empty, _allowHibernate),
            (string.Empty, _allowSignOut),
            ("Shutdown/restart delay (seconds)", _shutdownDelay)));
        list.Controls.Add(Group(_display));
        list.Controls.Add(Group(_audio));
        list.Controls.Add(Group(_metrics, ("Update every (seconds)", _metricsInterval)));
        list.Controls.Add(Group(_notifications));
        list.Controls.Add(Group(
            _mediaPlayer,
            ("While Home Assistant speaks", _interruption),
            ("Lower other apps to (%)", _duckLevel),
            (string.Empty, _speakingNotice)));
        list.Controls.Add(Group(
            _hardware,
            ("LibreHardwareMonitor port", _lhmPort),
            ("User name", _lhmUsername),
            ("Password", _lhmPassword),
            ("Read every (seconds)", _lhmInterval)));
        if (_draft.HasHardwarePassword)
        {
            _lhmPassword.PlaceholderText = PasswordSavedHint;
        }

        _interruption.SelectedIndexChanged += (_, _) => _duckLevel.Enabled = _mediaPlayer.Checked && _interruption.SelectedIndex == 0;
        _mediaPlayer.CheckedChanged += (_, _) => _duckLevel.Enabled = _mediaPlayer.Checked && _interruption.SelectedIndex == 0;
        list.Resize += (_, _) =>
        {
            foreach (Control group in list.Controls)
            {
                group.Width = list.ClientSize.Width - list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth;
            }
        };

        return list;
    }

    private Control GeneralTab(IStartupRegistration startup)
    {
        var grid = Grid();
        Row(grid, string.Empty, _startWithWindows);
        if (!startup.IsInstalled)
        {
            var note = Note();
            note.Text = "This copy isn't installed, so this setting takes effect once Smart Home Relay is installed.";
            Row(grid, string.Empty, note);
        }

        Row(grid, "Log level", _logLevel);

        var openFile = new Button { Text = "Open settings.json", AutoSize = true };
        openFile.Click += (_, _) => _shell.OpenFile(_paths.SettingsFilePath);
        var openLogs = new Button { Text = "Open logs folder", AutoSize = true };
        openLogs.Click += (_, _) => _shell.OpenFolder(_paths.LogsDirectory);
        var links = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        links.Controls.Add(openFile);
        links.Controls.Add(openLogs);
        Row(grid, string.Empty, links);

        var advanced = Note();
        advanced.Text = "Other options (MQTT topics, download limits, hardware sensor list and overrides) are in settings.json. "
            + "Saving here keeps them, but removes comments from the file.";
        Row(grid, string.Empty, advanced);
        return grid;
    }

    private void Populate(AppSettings s)
    {
        _host.Text = s.Mqtt.Host;
        _port.Value = Clamp(_port, s.Mqtt.Port);
        _username.Text = s.Mqtt.Username;

        _deviceName.Text = s.Device.Name;
        _deviceId.Text = s.Device.Id;

        var f = s.Features;
        _presence.Checked = f.Presence.Enabled;
        _idleSeconds.Value = Clamp(_idleSeconds, f.Presence.IdleThresholdSeconds);
        _power.Checked = f.Power.Enabled;
        _allowShutdown.Checked = f.Power.AllowShutdown;
        _allowRestart.Checked = f.Power.AllowRestart;
        _allowHibernate.Checked = f.Power.AllowHibernate;
        _allowSignOut.Checked = f.Power.AllowSignOut;
        _shutdownDelay.Value = Clamp(_shutdownDelay, f.Power.ShutdownDelaySeconds);
        _display.Checked = f.Display.Enabled;
        _audio.Checked = f.Audio.Enabled;
        _metrics.Checked = f.Metrics.Enabled;
        _metricsInterval.Value = Clamp(_metricsInterval, f.Metrics.IntervalSeconds);
        _notifications.Checked = f.Notifications.Enabled;
        _mediaPlayer.Checked = f.MediaPlayer.Enabled;
        _interruption.SelectedIndex = f.MediaPlayer.Interruption == MediaPlayerSettings.InterruptionPause ? 1 : 0;
        _duckLevel.Value = Clamp(_duckLevel, f.MediaPlayer.DuckLevelPercent);
        _speakingNotice.Checked = f.MediaPlayer.ShowNotification;
        _hardware.Checked = f.Hardware.Enabled;
        _lhmPort.Value = Clamp(_lhmPort, f.Hardware.Port);
        _lhmUsername.Text = f.Hardware.Username;
        _lhmInterval.Value = Clamp(_lhmInterval, f.Hardware.IntervalSeconds);

        _startWithWindows.Checked = s.StartWithWindows;
        _logLevel.SelectedItem = s.LogLevel.ToString();
        if (_logLevel.SelectedIndex < 0)
        {
            _logLevel.SelectedItem = nameof(LogLevel.Information);
        }

        // Fire the handlers once so dependent options start enabled or disabled correctly.
        foreach (var feature in new[] { _presence, _power, _display, _audio, _metrics, _notifications, _mediaPlayer, _hardware })
        {
            feature.Checked = !feature.Checked;
            feature.Checked = !feature.Checked;
        }
    }

    /// <summary>The window's values written into <paramref name="s"/>; everything not shown is left as it was.</summary>
    private void ReadInto(AppSettings s)
    {
        s.Mqtt.Host = _host.Text;
        s.Mqtt.Port = (int)_port.Value;
        s.Mqtt.Username = _username.Text.Trim();

        s.Device.Name = _deviceName.Text;
        s.Device.Id = _deviceId.Text;

        var f = s.Features;
        f.Presence.Enabled = _presence.Checked;
        f.Presence.IdleThresholdSeconds = (int)_idleSeconds.Value;
        f.Power.Enabled = _power.Checked;
        f.Power.AllowShutdown = _allowShutdown.Checked;
        f.Power.AllowRestart = _allowRestart.Checked;
        f.Power.AllowHibernate = _allowHibernate.Checked;
        f.Power.AllowSignOut = _allowSignOut.Checked;
        f.Power.ShutdownDelaySeconds = (int)_shutdownDelay.Value;
        f.Display.Enabled = _display.Checked;
        f.Audio.Enabled = _audio.Checked;
        f.Metrics.Enabled = _metrics.Checked;
        f.Metrics.IntervalSeconds = (int)_metricsInterval.Value;
        f.Notifications.Enabled = _notifications.Checked;
        f.MediaPlayer.Enabled = _mediaPlayer.Checked;
        f.MediaPlayer.Interruption = _interruption.SelectedIndex == 1 ? MediaPlayerSettings.InterruptionPause : MediaPlayerSettings.InterruptionDuck;
        f.MediaPlayer.DuckLevelPercent = (int)_duckLevel.Value;
        f.MediaPlayer.ShowNotification = _speakingNotice.Checked;
        f.Hardware.Enabled = _hardware.Checked;
        f.Hardware.Port = (int)_lhmPort.Value;
        f.Hardware.Username = _lhmUsername.Text.Trim();
        f.Hardware.IntervalSeconds = (int)_lhmInterval.Value;

        s.StartWithWindows = _startWithWindows.Checked;
        s.LogLevel = Enum.Parse<LogLevel>((string)_logLevel.SelectedItem!);
    }

    private async void OnTestClicked(object? sender, EventArgs e)
    {
        _test.Enabled = false;
        _testResult.ForeColor = SystemColors.ControlText;
        _testResult.Text = "Connecting…";
        try
        {
            var settings = SettingsEditorCopy();
            var result = await _tester.TestAsync(settings.Mqtt, settings.Device, _password.Text, CancellationToken.None);
            _testResult.ForeColor = result.Succeeded ? Color.ForestGreen : Color.Firebrick;
            _testResult.Text = result.Message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _testResult.ForeColor = Color.Firebrick;
            _testResult.Text = "The test failed unexpectedly. The log has details.";
        }
        finally
        {
            if (!IsDisposed)
            {
                _test.Enabled = true;
            }
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        _save.Enabled = false;
        _problems.Text = string.Empty;
        try
        {
            var settings = SettingsEditorCopy();
            var problems = await _editor.SaveAsync(settings, _password.Text, _lhmPassword.Text, CancellationToken.None);
            if (problems.Count == 0)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _problems.Text = string.Join("\n", problems);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _problems.Text = $"Could not save settings.json: {ex.Message}";
        }
        finally
        {
            if (!IsDisposed)
            {
                _save.Enabled = true;
            }
        }
    }

    /// <summary>
    /// The settings as loaded when the window opened (so everything it doesn't show is kept) with the window's
    /// values written in.
    /// </summary>
    private AppSettings SettingsEditorCopy()
    {
        var settings = SettingsEditor.Clone(_draft.Settings);
        ReadInto(settings);
        return settings;
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(8), UseVisualStyleBackColor = true };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private static TableLayoutPanel Grid()
    {
        var grid = new TableLayoutPanel { ColumnCount = 2, AutoScroll = true, Padding = new Padding(4) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static void Row(TableLayoutPanel grid, string label, Control control)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (label.Length > 0)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 3) }, 0, row);
        }

        control.Margin = new Padding(3);
        if (control is TextBox or ComboBox)
        {
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        }

        grid.Controls.Add(control, 1, row);
    }

    private static GroupBox Group(CheckBox enabled, params (string Label, Control Control)[] options)
    {
        var grid = Grid();
        grid.AutoScroll = false;
        grid.AutoSize = true;
        grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        grid.Dock = DockStyle.Top;
        foreach (var (label, control) in options)
        {
            Row(grid, label, control);
        }

        var box = new GroupBox { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), Margin = new Padding(0, 0, 0, 8), MinimumSize = new Size(400, 0) };
        enabled.Dock = DockStyle.Top;
        box.Controls.Add(grid);
        box.Controls.Add(enabled);
        enabled.CheckedChanged += (_, _) => grid.Enabled = enabled.Checked;
        return box;
    }

    private static TextBox TextField() => new() { Width = 240 };

    private static TextBox Password() => new() { Width = 240, UseSystemPasswordChar = true };

    private static NumericUpDown Number(int min, long max) => new() { Minimum = min, Maximum = max, Width = 100, ThousandsSeparator = false };

    private static CheckBox Feature(string text) => new() { Text = text, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold) };

    private static CheckBox Option(string text) => new() { Text = text, AutoSize = true };

    private static ComboBox Choice(params string[] items)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        box.Items.AddRange(items);
        return box;
    }

    private static Label Note() => new() { AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = SystemColors.GrayText };

    private static decimal Clamp(NumericUpDown box, long value) => Math.Clamp(value, box.Minimum, box.Maximum);

    /// <summary>Keeps wrapped notes as wide as the window.</summary>
    private static void FitNotes(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Label { AutoSize: true, MaximumSize.Width: > 0 } label)
            {
                label.MaximumSize = new Size(Math.Max(200, parent.ClientSize.Width - 40), 0);
            }

            FitNotes(child);
        }
    }
}
