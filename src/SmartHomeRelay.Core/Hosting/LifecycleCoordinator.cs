using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Hosting;

/// <summary>
/// Turns OS power, session, and network events into connection and module actions (spec 6):
/// <list type="bullet">
/// <item><b>Suspend:</b> publish <c>offline</c>, disconnect, pause modules; all within the ~2s budget.</item>
/// <item><b>Resume:</b> resume modules, then reconnect (republishing online, discovery, and full state).</item>
/// <item><b>Shutdown / sign-out:</b> publish <c>offline</c>, disconnect, then exit the app.</item>
/// <item><b>Network available:</b> retry the connection now instead of waiting out the backoff.</item>
/// </list>
/// </summary>
public sealed partial class LifecycleCoordinator(
    ISystemEventSource events,
    MqttConnectionSupervisor connection,
    ModuleHost modules,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<LifecycleCoordinator> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        events.Suspending += OnSuspending;
        events.Resumed += OnResumed;
        events.SessionEnding += OnSessionEnding;
        events.SessionSwitched += OnSessionSwitched;
        events.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        events.Suspending -= OnSuspending;
        events.Resumed -= OnResumed;
        events.SessionEnding -= OnSessionEnding;
        events.SessionSwitched -= OnSessionSwitched;
        events.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        return Task.CompletedTask;
    }

    private void OnSuspending(object? sender, SystemTransitionEventArgs e)
    {
        LogSuspending(logger);
        e.Defer(GoOfflineAsync(pauseModules: true));
    }

    private void OnResumed(object? sender, EventArgs e)
    {
        LogResumed(logger);
        _ = ResumeAsync();
    }

    private void OnSessionEnding(object? sender, SystemTransitionEventArgs e)
    {
        LogSessionEnding(logger);
        e.Defer(ShutDownAsync());
    }

    private void OnSessionSwitched(object? sender, SessionSwitchKind kind) =>
        // The Presence module (milestone 4) reacts to these; here they are only logged for diagnostics.
        LogSessionSwitched(logger, kind);

    private void OnNetworkAvailabilityChanged(object? sender, bool available)
    {
        LogNetworkChanged(logger, available);
        if (available)
        {
            connection.NotifyNetworkAvailable();
        }
    }

    /// <summary>Offline first (most important within the budget), then pause modules.</summary>
    private async Task GoOfflineAsync(bool pauseModules)
    {
        using var budget = new CancellationTokenSource(SystemTransitionEventArgs.Budget, time);
        try
        {
            await connection.SuspendAsync(budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Out of time: the broker's last will reports offline once the socket drops.
            LogOfflineTimedOut(logger);
        }

        if (pauseModules)
        {
            await modules.NotifySuspendingAsync(budget.Token).ConfigureAwait(false);
        }
    }

    private async Task ResumeAsync()
    {
        try
        {
            // Modules first, so the state they publish on resume is sent as soon as we reconnect.
            await modules.NotifyResumedAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            connection.Resume();
        }
    }

    private async Task ShutDownAsync()
    {
        await GoOfflineAsync(pauseModules: false).ConfigureAwait(false);

        // Windows is ending the session: exit cleanly. The host's normal stop runs next.
        lifetime.StopApplication();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "System is going to sleep")]
    private static partial void LogSuspending(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "System woke up")]
    private static partial void LogResumed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Windows is shutting down or the user is signing out; going offline and exiting")]
    private static partial void LogSessionEnding(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Session changed: {Kind}")]
    private static partial void LogSessionSwitched(ILogger logger, SessionSwitchKind kind);

    [LoggerMessage(Level = LogLevel.Information, Message = "Network availability changed: {Available}")]
    private static partial void LogNetworkChanged(ILogger logger, bool available);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not go offline within the suspend budget; the broker's last will covers it")]
    private static partial void LogOfflineTimedOut(ILogger logger);
}
