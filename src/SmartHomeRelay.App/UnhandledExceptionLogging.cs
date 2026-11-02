using Microsoft.Extensions.Logging;

namespace SmartHomeRelay.App;

/// <summary>Global exception handlers (spec 6): log, and keep the app alive where it is safe.</summary>
internal static class UnhandledExceptionLogging
{
    public static void Register(ILogger logger)
    {
        // UI thread exceptions are caught (SetUnhandledExceptionMode) so the tray stays up.
        Application.ThreadException += (_, e) => AppLog.UnhandledUiException(logger, e.Exception);

        // Background thread exceptions terminate the process; log them first.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.UnhandledDomainException(logger, e.ExceptionObject as Exception, e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.UnobservedTaskException(logger, e.Exception);
            e.SetObserved();
        };
    }
}
