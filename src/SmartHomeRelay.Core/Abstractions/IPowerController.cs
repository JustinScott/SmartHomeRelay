namespace SmartHomeRelay.Core.Abstractions;

/// <summary>
/// Power actions (spec 5.2). Behind an interface so tests never shut anything down. None of these need
/// elevation. Whether an action is allowed is decided by the caller, not here.
/// </summary>
public interface IPowerController
{
    /// <summary>True if hibernation is enabled on this machine (spec 5.2: Hibernate is only offered then).</summary>
    bool IsHibernateAvailable { get; }

    /// <summary>Shuts down after <paramref name="delay"/>, with Windows' standard countdown notice.</summary>
    Task ShutdownAsync(TimeSpan delay, CancellationToken cancellationToken);

    /// <summary>Restarts after <paramref name="delay"/>, with Windows' standard countdown notice.</summary>
    Task RestartAsync(TimeSpan delay, CancellationToken cancellationToken);

    /// <summary>Cancels a pending (delayed) shutdown or restart. Does nothing if none is pending.</summary>
    Task CancelShutdownAsync(CancellationToken cancellationToken);

    /// <summary>Puts the PC to sleep. Returns after it wakes.</summary>
    Task SleepAsync(CancellationToken cancellationToken);

    /// <summary>Hibernates the PC. Returns after it resumes.</summary>
    Task HibernateAsync(CancellationToken cancellationToken);

    /// <summary>Locks the session.</summary>
    Task LockAsync(CancellationToken cancellationToken);

    /// <summary>Signs the user out (closes this app too).</summary>
    Task SignOutAsync(CancellationToken cancellationToken);
}
