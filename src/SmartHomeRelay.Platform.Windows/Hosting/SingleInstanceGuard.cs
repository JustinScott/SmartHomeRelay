using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Platform.Windows.Hosting;

/// <summary>
/// Named mutex for single-instance enforcement plus a named event a second launch uses to
/// notify the first. Both are <c>Local\</c> (per logon session), so each signed-in user can run their own copy.
/// </summary>
public sealed class SingleInstanceGuard : ISingleInstanceGuard
{
    /// <summary>Name used by the app. Tests pass unique names.</summary>
    public const string DefaultName = "SmartHomeRelay";

    private readonly string _mutexName;
    private readonly EventWaitHandle _activationEvent;
    private Mutex? _mutex;
    private RegisteredWaitHandle? _registeredWait;
    private bool _disposed;

    /// <summary>Creates a guard for <paramref name="name"/>. Nothing is acquired until <see cref="TryAcquire"/>.</summary>
    public SingleInstanceGuard(string name = DefaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _mutexName = $@"Local\{name}.Instance";
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Activate");
    }

    /// <inheritdoc />
    public event EventHandler? SecondInstanceStarted;

    /// <inheritdoc />
    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_mutex is not null)
        {
            return true;
        }

        var mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => SecondInstanceStarted?.Invoke(this, EventArgs.Empty),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        return true;
    }

    /// <inheritdoc />
    public void SignalPrimaryInstance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _activationEvent.Set();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registeredWait?.Unregister(null);

        // Closing the handle is enough: the mutex object is destroyed when its last handle closes.
        // ReleaseMutex is skipped on purpose because it throws unless called on the owning thread.
        _mutex?.Dispose();
        _activationEvent.Dispose();
    }
}
