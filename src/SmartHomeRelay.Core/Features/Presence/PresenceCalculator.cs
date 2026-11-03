namespace SmartHomeRelay.Core.Features.Presence;

/// <summary>Pure presence rules (spec 5.1), kept separate from timing and I/O so they are easy to test.</summary>
public static class PresenceCalculator
{
    /// <summary>Below this much idle time, <c>idle_seconds</c> is reported in <see cref="FineStep"/> steps.</summary>
    public static readonly TimeSpan FineStepLimit = TimeSpan.FromMinutes(5);

    /// <summary>Reporting step for short idle times.</summary>
    public static readonly TimeSpan FineStep = TimeSpan.FromSeconds(5);

    /// <summary>Reporting step for long idle times.</summary>
    public static readonly TimeSpan CoarseStep = TimeSpan.FromMinutes(1);

    /// <summary>Published as <c>logged_in_user</c> when no user name is available (spec 5.1).</summary>
    public const string NoUser = "none";

    /// <summary>Derives everything the module publishes.</summary>
    public static PresenceSnapshot Calculate(bool locked, string userName, TimeSpan idle, TimeSpan threshold)
    {
        userName = string.IsNullOrWhiteSpace(userName) ? NoUser : userName;
        var userActive = !locked && idle < threshold;
        var state = locked ? PresenceStates.Locked : userActive ? PresenceStates.Active : PresenceStates.Idle;
        return new PresenceSnapshot(locked, userName, ReportIdleSeconds(idle), userActive, state);
    }

    /// <summary>
    /// Idle seconds rounded down to a reporting step: 5s under 5 minutes, then 1 minute. The value only
    /// changes when the published value would, which keeps MQTT traffic and HA history small: 0 while the
    /// user is active (nothing published), and at most one update a minute during long idle periods such
    /// as overnight. Without this, each PC would add about 17,000 history rows a day.
    /// </summary>
    public static long ReportIdleSeconds(TimeSpan idle)
    {
        if (idle <= TimeSpan.Zero)
        {
            return 0;
        }

        var step = idle < FineStepLimit ? FineStep : CoarseStep;
        var seconds = (long)idle.TotalSeconds;
        var stepSeconds = (long)step.TotalSeconds;
        return seconds - (seconds % stepSeconds);
    }
}
