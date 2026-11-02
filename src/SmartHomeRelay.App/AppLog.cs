using Microsoft.Extensions.Logging;

namespace SmartHomeRelay.App;

/// <summary>Source-generated log messages for the app shell.</summary>
internal static partial class AppLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Smart Home Relay {Version} starting")]
    public static partial void Starting(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Smart Home Relay stopped")]
    public static partial void Stopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Smart Home Relay failed and is exiting")]
    public static partial void Fatal(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on the UI thread; the app keeps running")]
    public static partial void UnhandledUiException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception (terminating: {IsTerminating})")]
    public static partial void UnhandledDomainException(ILogger logger, Exception? exception, bool isTerminating);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unobserved task exception")]
    public static partial void UnobservedTaskException(ILogger logger, Exception exception);
}
