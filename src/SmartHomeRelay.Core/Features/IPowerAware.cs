namespace SmartHomeRelay.Core.Features;

/// <summary>
/// Optional for modules (spec 3.3): react to sleep and wake, e.g. pause and restart polling loops.
/// Only called for modules that are running. Calls are isolated; a failure is logged.
/// </summary>
public interface IPowerAware
{
    /// <summary>
    /// The system is about to sleep. Keep this short: the whole app has about 2 seconds (spec 6), and
    /// publishing offline happens first.
    /// </summary>
    Task OnSuspendingAsync(CancellationToken cancellationToken);

    /// <summary>The system woke up. State published here is sent once the connection is back.</summary>
    Task OnResumedAsync(CancellationToken cancellationToken);
}
