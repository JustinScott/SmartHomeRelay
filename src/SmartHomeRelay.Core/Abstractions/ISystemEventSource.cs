namespace SmartHomeRelay.Core.Abstractions;

/// <summary>
/// Power, session, and network events from the OS (spec 6). Behind an interface so lifecycle logic can
/// be tested by raising fake events. Events may be raised on any thread.
/// </summary>
public interface ISystemEventSource
{
    /// <summary>
    /// The system is about to sleep or hibernate. Use <see cref="SystemTransitionEventArgs.Defer"/> to finish
    /// work (publish offline, disconnect) before it does.
    /// </summary>
    event EventHandler<SystemTransitionEventArgs>? Suspending;

    /// <summary>The system woke from sleep or hibernation.</summary>
    event EventHandler? Resumed;

    /// <summary>The user is signing out or Windows is shutting down. Deferral works as for <see cref="Suspending"/>.</summary>
    event EventHandler<SystemTransitionEventArgs>? SessionEnding;

    /// <summary>Lock, unlock, logon, logoff, and remote connect/disconnect.</summary>
    event EventHandler<SessionSwitchKind>? SessionSwitched;

    /// <summary>Raised with the new value when any network becomes available or all become unavailable.</summary>
    event EventHandler<bool>? NetworkAvailabilityChanged;

    /// <summary>True if any network interface is up.</summary>
    bool IsNetworkAvailable { get; }
}
