using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace SmartHomeRelay.App.Logging;

/// <summary>Rolling file log: daily, 14 days retained, size capped (spec 11).</summary>
internal static class SerilogSetup
{
    private const long FileSizeLimitBytes = 10L * 1024 * 1024;
    private const int RetainedFileCount = 14;

    public static Logger CreateLogger(string logsDirectory, LoggingLevelSwitch levelSwitch) =>
        new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(logsDirectory, "smarthomerelay-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCount,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
}
