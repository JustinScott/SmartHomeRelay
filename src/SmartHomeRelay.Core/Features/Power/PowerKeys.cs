namespace SmartHomeRelay.Core.Features.Power;

/// <summary>Power entity keys (spec 5.2). Shipped keys are permanent: never rename them.</summary>
public static class PowerKeys
{
    /// <summary>button: shut down after the configured delay. Needs <c>allowShutdown</c>.</summary>
    public const string Shutdown = "shutdown";

    /// <summary>button: restart after the configured delay. Needs <c>allowRestart</c>.</summary>
    public const string Restart = "restart";

    /// <summary>button: cancel a pending shutdown or restart.</summary>
    public const string CancelShutdown = "cancel_shutdown";

    /// <summary>button: sleep.</summary>
    public const string Sleep = "sleep";

    /// <summary>button: hibernate. Only offered if hibernation is enabled; needs <c>allowHibernate</c>.</summary>
    public const string Hibernate = "hibernate";

    /// <summary>button: lock the session.</summary>
    public const string Lock = "lock";

    /// <summary>button: sign out. Needs <c>allowSignOut</c>.</summary>
    public const string SignOut = "sign_out";

    /// <summary>sensor (timestamp): when Windows started.</summary>
    public const string LastBoot = "last_boot";

    /// <summary>sensor (duration, seconds): time since Windows started.</summary>
    public const string Uptime = "uptime";
}
