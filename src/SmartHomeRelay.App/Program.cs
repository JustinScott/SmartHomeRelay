using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Extensions.Logging;
using SmartHomeRelay.App.Logging;
using SmartHomeRelay.App.Tray;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Features;
using SmartHomeRelay.Core.Features.Presence;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Platform.Windows;
using SmartHomeRelay.Platform.Windows.Configuration;
using SmartHomeRelay.Platform.Windows.Hosting;
using SmartHomeRelay.Platform.Windows.Presence;

namespace SmartHomeRelay.App;

/// <summary>Entry point and composition root (spec 3.2). Wiring only; no business logic.</summary>
internal static class Program
{
    private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromSeconds(5);

    [STAThread]
    private static int Main()
    {
        using var singleInstance = new SingleInstanceGuard();
        if (!singleInstance.TryAcquire())
        {
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

        // Feature modules (spec 13): the module plus its platform implementations.
        // Presence (spec 5.1)
        builder.Services.AddSingleton<IIdleTimeProvider, WindowsIdleTimeProvider>();
        builder.Services.AddSingleton<ISessionStateProvider, WindowsSessionStateProvider>();
        builder.Services.AddSingleton<IFeatureModule, PresenceModule>();

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SmartHomeRelay.App");
        UnhandledExceptionLogging.Register(logger);

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
            AppLog.Stopped(logger);
        }
    }
}
