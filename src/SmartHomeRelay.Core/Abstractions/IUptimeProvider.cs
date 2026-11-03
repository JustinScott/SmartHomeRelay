namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Time since Windows started, including time spent asleep (spec 5.2: last_boot, uptime).</summary>
public interface IUptimeProvider
{
    /// <summary>Time since boot.</summary>
    TimeSpan GetUptime();
}
