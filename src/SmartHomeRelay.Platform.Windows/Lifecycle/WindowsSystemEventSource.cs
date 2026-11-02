using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Platform.Windows.Lifecycle;

/// <summary>
/// <see cref="ISystemEventSource"/> over <see cref="SystemEvents"/> and <see cref="NetworkChange"/> (spec 6).
/// A thin OS wrapper: all decisions live in Core's LifecycleCoordinator.
/// </summary>
/// <remarks>
/// SystemEvents raises its events on its own hidden-window thread. For suspend and session ending, Windows
/// continues as soon as the handler returns, so this is the one place that blocks: it waits (up to the
/// budget) for the work handlers deferred, such as publishing offline.
/// </remarks>
public sealed partial class WindowsSystemEventSource : ISystemEventSource, IDisposable
{
    private readonly ILogger<WindowsSystemEventSource> _logger;
    private readonly TimeSpan _budget;
    private bool _disposed;

    /// <summary>Subscribes to the OS events.</summary>
    public WindowsSystemEventSource(ILogger<WindowsSystemEventSource> logger)
        : this(logger, SystemTransitionEventArgs.Budget)
    {
    }

    internal WindowsSystemEventSource(ILogger<WindowsSystemEventSource> logger, TimeSpan budget)
    {
        _logger = logger;
        _budget = budget;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.SessionEnding += OnSessionEnding;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    /// <inheritdoc />
    public event EventHandler<SystemTransitionEventArgs>? Suspending;

    /// <inheritdoc />
    public event EventHandler? Resumed;

    /// <inheritdoc />
    public event EventHandler<SystemTransitionEventArgs>? SessionEnding;

    /// <inheritdoc />
    public event EventHandler<SessionSwitchKind>? SessionSwitched;

    /// <inheritdoc />
    public event EventHandler<bool>? NetworkAvailabilityChanged;

    /// <inheritdoc />
    public bool IsNetworkAvailable => NetworkInterface.GetIsNetworkAvailable();

    /// <summary>Unsubscribes. SystemEvents events are static, so not unsubscribing would leak this object.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.SessionEnding -= OnSessionEnding;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
    }

    internal static SessionSwitchKind Map(SessionSwitchReason reason) => reason switch
    {
        SessionSwitchReason.SessionLock => SessionSwitchKind.Lock,
        SessionSwitchReason.SessionUnlock => SessionSwitchKind.Unlock,
        SessionSwitchReason.SessionLogon => SessionSwitchKind.Logon,
        SessionSwitchReason.SessionLogoff => SessionSwitchKind.Logoff,
        SessionSwitchReason.ConsoleConnect => SessionSwitchKind.ConsoleConnect,
        SessionSwitchReason.ConsoleDisconnect => SessionSwitchKind.ConsoleDisconnect,
        SessionSwitchReason.RemoteConnect => SessionSwitchKind.RemoteConnect,
        SessionSwitchReason.RemoteDisconnect => SessionSwitchKind.RemoteDisconnect,
        _ => SessionSwitchKind.Other,
    };

    /// <summary>Raises a transition event and waits, up to the budget, for the work handlers deferred.</summary>
    /// <returns>True if all deferred work finished within the budget.</returns>
    private bool RaiseTransition(EventHandler<SystemTransitionEventArgs>? handler, string name)
    {
        var args = new SystemTransitionEventArgs();
        handler?.Invoke(this, args);

        // OS boundary, deliberately synchronous: Windows proceeds the moment this handler returns.
        // Completion never faults (failures are observed per task), so Wait only returns true or false.
        var finished = args.Completion.Wait(_budget);
        if (!finished)
        {
            LogTransitionTimedOut(_logger, name, _budget.TotalSeconds);
        }

        return finished;
    }

    /// <summary>The suspend path used by the OS handler, callable from tests without suspending the PC.</summary>
    internal bool RaiseSuspending() => RaiseTransition(Suspending, "suspend");

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) => Guard("power mode change", () =>
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                RaiseSuspending();
                break;
            case PowerModes.Resume:
                Resumed?.Invoke(this, EventArgs.Empty);
                break;
            default:
                break; // StatusChange (battery/AC) is not used in v1.
        }
    });

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) =>
        Guard("session ending", () => RaiseTransition(SessionEnding, "session ending"));

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) =>
        Guard("session switch", () => SessionSwitched?.Invoke(this, Map(e.Reason)));

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        Guard("network change", () => NetworkAvailabilityChanged?.Invoke(this, e.IsAvailable));

    /// <summary>An exception on the SystemEvents thread would crash the app (spec: never crash). Log it instead.</summary>
    private void Guard(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            LogHandlerFailed(_logger, ex, name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Work for {Transition} did not finish within {BudgetSeconds}s; Windows continues anyway")]
    private static partial void LogTransitionTimedOut(ILogger logger, string transition, double budgetSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling a system {Event} event failed")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string @event);
}
