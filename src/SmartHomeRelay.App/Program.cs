using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Extensions.Logging;
using SmartHomeRelay.App.Logging;
using SmartHomeRelay.App.Settings;
using SmartHomeRelay.App.Tray;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Features;
using SmartHomeRelay.Core.Features.Audio;
using SmartHomeRelay.Core.Features.Connection;
using SmartHomeRelay.Core.Features.Display;
using SmartHomeRelay.Core.Features.Hardware;
using SmartHomeRelay.Core.Features.MediaPlayer;
using SmartHomeRelay.Core.Features.Notifications;
using SmartHomeRelay.Core.Features.Metrics;
using SmartHomeRelay.Core.Features.Power;
using SmartHomeRelay.Core.Features.Presence;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Net;
using SmartHomeRelay.Platform.Windows;
using SmartHomeRelay.Platform.Windows.Audio;
using SmartHomeRelay.Platform.Windows.Configuration;
using SmartHomeRelay.Platform.Windows.Display;
using SmartHomeRelay.Platform.Windows.Hardware;
using SmartHomeRelay.Platform.Windows.Hosting;
using SmartHomeRelay.Platform.Windows.Media;
using SmartHomeRelay.Platform.Windows.Metrics;
using SmartHomeRelay.Platform.Windows.Notifications;
using SmartHomeRelay.Platform.Windows.Power;
using SmartHomeRelay.Platform.Windows.Presence;

namespace SmartHomeRelay.App;

/// <summary>Entry point and composition root (spec 3.2). Wiring only; no business logic.</summary>
internal static class Program
{
    /// <summary>Run by the uninstaller: remove this PC from Home Assistant and exit, with no window (spec 4.4, 8).</summary>
    private const string RemoveArgument = "--remove-from-home-assistant";

    private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_removalTimeout = TimeSpan.FromSeconds(20);

    [STAThread]
    private static int Main(string[] args)
    {
        var removeOnly = args.Contains(RemoveArgument, StringComparer.OrdinalIgnoreCase);
        using var singleInstance = new SingleInstanceGuard();
        if (!singleInstance.TryAcquire())
        {
            if (removeOnly)
            {
                // The running copy would announce everything again.
                return RemovalExitCodes.AlreadyRunning;
            }

            // The running copy shows a tray balloon in response.
            singleInstance.SignalPrimaryInstance();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        var paths = new WindowsAppPaths();
        var levelSwitch = new LoggingLevelSwitch();

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddRelaySettingsFile(paths.SettingsFilePath);

        builder.Logging.SetMinimumLevel(LogLevel.Trace); // Serilog's level switch does the filtering.
        builder.Logging.AddProvider(new SerilogLoggerProvider(SerilogSetup.CreateLogger(paths.LogsDirectory, levelSwitch), dispose: true));

        builder.Services.AddSingleton(levelSwitch);
        builder.Services.AddSingleton<TrayNotifier>();
        builder.Services.AddSingleton<IUserNotifier>(sp => sp.GetRequiredService<TrayNotifier>());
        builder.Services.AddWindowsPlatform(paths, singleInstance);
        builder.Services.AddRelaySettings(builder.Configuration);
        builder.Services.AddHostedService<LogLevelSynchronizer>();
        builder.Services.AddRelayMqtt();
        builder.Services.AddSingleton<TrayApplicationContext>();
        builder.Services.AddSingleton<RemovalRequest>();

        // Settings window and start with Windows (spec 7.1)
        builder.Services.AddSingleton<SettingsWindowOpener>();
        builder.Services.AddSingleton<ISettingsWindow>(sp => sp.GetRequiredService<SettingsWindowOpener>());
        builder.Services.AddTransient<SettingsForm>();
        builder.Services.AddSingleton<Func<SettingsForm>>(sp => () => sp.GetRequiredService<SettingsForm>());
        builder.Services.AddSingleton<IStartupRegistration, WindowsStartupRegistration>();
        builder.Services.AddHostedService<StartWithWindowsSynchronizer>();

        // Feature modules (spec 13): the module plus its platform implementations.
        // Connected (spec 4.3)
        builder.Services.AddSingleton<IFeatureModule, ConnectionModule>();

        // Presence (spec 5.1)
        builder.Services.AddSingleton<IIdleTimeProvider, WindowsIdleTimeProvider>();
        builder.Services.AddSingleton<ISessionStateProvider, WindowsSessionStateProvider>();
        builder.Services.AddSingleton<IFeatureModule, PresenceModule>();

        // Power (spec 5.2)
        builder.Services.AddSingleton<IPowerController, WindowsPowerController>();
        builder.Services.AddSingleton<IUptimeProvider, WindowsUptimeProvider>();
        builder.Services.AddSingleton<IFeatureModule, PowerModule>();

        // Metrics (spec 5.5)
        builder.Services.AddSingleton<ISystemMetricsSource, WindowsSystemMetricsSource>();
        builder.Services.AddSingleton<IFeatureModule, MetricsModule>();

        // Display (spec 5.3)
        builder.Services.AddSingleton<IDisplayController, WindowsDisplayController>();
        builder.Services.AddSingleton<IFeatureModule, DisplayModule>();

        // Audio (spec 5.4)
        builder.Services.AddSingleton<IAudioController, WindowsAudioController>();
        builder.Services.AddSingleton<IFeatureModule, AudioModule>();

        // Hardware sensors from a user-managed LibreHardwareMonitor (spec 5.8)
        builder.Services.AddSingleton<ILibreHardwareMonitorEndpoint, WindowsLibreHardwareMonitorEndpoint>();
        builder.Services.AddSingleton<IFeatureModule, HardwareModule>();

        // Toasts (spec 5.6), shared by Notifications and the media player's speaking notice
        builder.Services.AddSingleton(new ToastAppIdentity(ToastAppIdentity.DefaultAppUserModelId, "Smart Home Relay", ToastIconFile.Write(paths.CacheDirectory)));
        builder.Services.AddSingleton<IToastPresenter, WindowsToastPresenter>();
        builder.Services.AddSingleton<IHttpDownloader, LimitedHttpDownloader>();
        builder.Services.AddSingleton<IFeatureModule, NotificationsModule>();

        // Media player through the MQTT Media Player add-on (spec 5.9); volume and mute reuse IAudioController
        builder.Services.AddSingleton<IMediaSessionSource, WindowsMediaSessionSource>();
        builder.Services.AddSingleton<IAnnouncementPlayer, WindowsAnnouncementPlayer>();
        builder.Services.AddSingleton<IAudioDucker, WindowsAudioDucker>();
        builder.Services.AddSingleton<IFeatureModule, MediaPlayerModule>();

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SmartHomeRelay.App");
        UnhandledExceptionLogging.Register(logger);

        if (removeOnly)
        {
            return RemoveFromHomeAssistant(host.Services, logger).Outcome == RemovalOutcome.Failed
                ? RemovalExitCodes.Failed
                : RemovalExitCodes.Done;
        }

        try
        {
            AppLog.Starting(logger, typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");

            // Main must stay synchronous to keep this thread STA for WinForms. Running the async host
            // calls on the thread pool means no UI synchronization context is captured, so this
            // blocking bridge cannot deadlock. It is the only one in the app.
            Task.Run(() => host.StartAsync()).GetAwaiter().GetResult();

            var tray = host.Services.GetRequiredService<TrayApplicationContext>();
            Application.Run(tray);
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Fatal(logger, ex);
            return 1;
        }
        finally
        {
            Task.Run(() => host.StopAsync(s_shutdownTimeout)).GetAwaiter().GetResult();

            // Tray menu → Remove from Home Assistant: only after our own connection has said goodbye.
            if (host.Services.GetRequiredService<RemovalRequest>().Requested)
            {
                var result = RemoveFromHomeAssistant(host.Services, logger);
                MessageBox.Show(result.Message, "Smart Home Relay", MessageBoxButtons.OK, result.Outcome == RemovalOutcome.Failed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }

            AppLog.Stopped(logger);
        }
    }

    /// <summary>Reads the settings and clears what this PC left on the broker. Never throws for broker problems.</summary>
    private static RemovalResult RemoveFromHomeAssistant(IServiceProvider services, ILogger logger)
    {
        // Same thread-pool bridge as above. The host isn't started in remove-only mode, so read the settings once.
        return Task.Run(async () =>
        {
            var state = services.GetRequiredService<SettingsState>();
            await state.StartAsync(CancellationToken.None).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(s_removalTimeout);
            try
            {
                return await services.GetRequiredService<HomeAssistantRemover>().RemoveAsync(state.Current.Settings, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                AppLog.RemovalTimedOut(logger);
                return new RemovalResult(RemovalOutcome.Failed, 0, "The MQTT broker didn't answer in time, so this PC may still be in Home Assistant.");
            }
        }).GetAwaiter().GetResult();
    }
}

/// <summary>Exit codes of <c>--remove-from-home-assistant</c>, read by the uninstaller.</summary>
internal static class RemovalExitCodes
{
    /// <summary>Removed, or nothing was set up.</summary>
    public const int Done = 0;

    /// <summary>The broker couldn't be reached or refused, or the settings can't be used.</summary>
    public const int Failed = 1;

    /// <summary>Smart Home Relay is running, so nothing was done.</summary>
    public const int AlreadyRunning = 2;
}

/// <summary>Set by the tray menu: remove this PC from Home Assistant once the app has stopped (spec 4.4).</summary>
internal sealed class RemovalRequest
{
    public bool Requested { get; set; }
}
