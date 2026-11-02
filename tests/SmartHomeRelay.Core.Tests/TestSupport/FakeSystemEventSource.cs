using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>Raises OS events on demand (spec 6: lifecycle logic is tested with fake events).</summary>
internal sealed class FakeSystemEventSource : ISystemEventSource
{
    public event EventHandler<SystemTransitionEventArgs>? Suspending;

    public event EventHandler? Resumed;

    public event EventHandler<SystemTransitionEventArgs>? SessionEnding;

    public event EventHandler<SessionSwitchKind>? SessionSwitched;

    public event EventHandler<bool>? NetworkAvailabilityChanged;

    public bool IsNetworkAvailable { get; set; } = true;

    public bool HasSubscribers => Suspending is not null || Resumed is not null || SessionEnding is not null
        || SessionSwitched is not null || NetworkAvailabilityChanged is not null;

    /// <summary>Raises Suspending; returns a task that completes when the deferred work is done.</summary>
    public Task RaiseSuspending()
    {
        var args = new SystemTransitionEventArgs();
        Suspending?.Invoke(this, args);
        return args.Completion;
    }

    public void RaiseResumed() => Resumed?.Invoke(this, EventArgs.Empty);

    public Task RaiseSessionEnding()
    {
        var args = new SystemTransitionEventArgs();
        SessionEnding?.Invoke(this, args);
        return args.Completion;
    }

    public void RaiseSessionSwitched(SessionSwitchKind kind) => SessionSwitched?.Invoke(this, kind);

    public void RaiseNetworkAvailabilityChanged(bool available)
    {
        IsNetworkAvailable = available;
        NetworkAvailabilityChanged?.Invoke(this, available);
    }
}
