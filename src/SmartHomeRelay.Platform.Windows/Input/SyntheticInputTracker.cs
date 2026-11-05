namespace SmartHomeRelay.Platform.Windows.Input;

/// <summary>
/// Remembers input the app injected itself (Monitor on's mouse nudge, spec 5.3) so Presence doesn't mistake
/// it for the user coming back. Idle time is then measured from the last real input, until the user
/// actually provides new input. Shared by the display controller and the idle-time provider.
/// </summary>
public sealed class SyntheticInputTracker
{
    private readonly Lock _gate = new();
    private uint? _pendingRealInput;
    private (uint RealInput, uint SyntheticInput)? _injection;

    /// <summary>
    /// Call just before injecting, with the last-input tick read then. Until <see cref="Complete"/>, idle time
    /// is measured from that tick, so a Presence sample taken mid-injection doesn't see "active".
    /// </summary>
    public void Begin(uint lastInputBefore)
    {
        lock (_gate)
        {
            _pendingRealInput = lastInputBefore;
            _injection = null;
        }
    }

    /// <summary>
    /// Call after injecting, with the last-input tick Windows reports for the injected input. If the injection
    /// didn't register as input (e.g. on the secure desktop), there is nothing to hide.
    /// </summary>
    public void Complete(uint lastInputAfter)
    {
        lock (_gate)
        {
            if (_pendingRealInput is { } real && lastInputAfter != real)
            {
                _injection = (real, lastInputAfter);
            }

            _pendingRealInput = null;
        }
    }

    /// <summary>
    /// The last-input tick to measure idle time from: the real one while the newest input is our injection,
    /// otherwise <paramref name="lastInput"/> unchanged (and the injection is forgotten).
    /// </summary>
    public uint EffectiveLastInput(uint lastInput)
    {
        lock (_gate)
        {
            if (_pendingRealInput is { } pending)
            {
                return pending;
            }

            if (_injection is { } injection)
            {
                if (lastInput == injection.SyntheticInput)
                {
                    return injection.RealInput;
                }

                _injection = null; // Newer input than ours: the user is really back.
            }

            return lastInput;
        }
    }
}
