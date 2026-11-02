namespace SmartHomeRelay.Core.Abstractions;

/// <summary>
/// A system transition the app must react to before it happens (suspend, shutdown, sign-out).
/// Handlers call <see cref="Defer"/> synchronously with the work to finish; the event source then
/// waits for that work, up to <see cref="Budget"/>, before letting Windows continue (spec 6).
/// </summary>
public sealed class SystemTransitionEventArgs : EventArgs
{
    /// <summary>How long Windows is held for (spec 6: best effort, about 2 seconds).</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    private readonly List<Task> _work = [];
    private readonly Lock _gate = new();

    /// <summary>Registers work that should finish before the transition. Call only from the event handler.</summary>
    public void Defer(Task work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            _work.Add(work);
        }
    }

    /// <summary>Completes when all deferred work has finished (successfully or not).</summary>
    public Task Completion
    {
        get
        {
            lock (_gate)
            {
                return Task.WhenAll(_work.Select(t => t.ContinueWith(_ => { }, TaskScheduler.Default)));
            }
        }
    }
}
