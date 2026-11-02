namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Ensures only one copy of the app runs per user session (spec 6, App start).</summary>
public interface ISingleInstanceGuard : IDisposable
{
    /// <summary>
    /// Raised in the primary instance when another launch was attempted.
    /// Raised on a background thread; handlers must marshal to the UI thread themselves.
    /// </summary>
    event EventHandler? SecondInstanceStarted;

    /// <summary>Tries to become the primary instance. Returns false if another instance already is.</summary>
    bool TryAcquire();

    /// <summary>Called by a second launch to tell the primary instance it was started again.</summary>
    void SignalPrimaryInstance();
}
