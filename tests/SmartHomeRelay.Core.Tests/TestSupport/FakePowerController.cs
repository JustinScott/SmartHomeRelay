using System.Collections.Concurrent;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>Records power actions instead of performing them (AGENTS.md: never run destructive OS actions in tests).</summary>
internal sealed class FakePowerController : IPowerController, IUptimeProvider
{
    public bool IsHibernateAvailable { get; set; } = true;

    public TimeSpan Uptime { get; set; } = TimeSpan.FromHours(3);

    /// <summary>Every action, e.g. "shutdown 60s", "sleep".</summary>
    public ConcurrentQueue<string> Actions { get; } = new();

    public Exception? ThrowOnNextAction { get; set; }

    public TimeSpan GetUptime() => Uptime;

    public Task ShutdownAsync(TimeSpan delay, CancellationToken cancellationToken) => Record($"shutdown {delay.TotalSeconds}s");

    public Task RestartAsync(TimeSpan delay, CancellationToken cancellationToken) => Record($"restart {delay.TotalSeconds}s");

    public Task CancelShutdownAsync(CancellationToken cancellationToken) => Record("cancel");

    public Task SleepAsync(CancellationToken cancellationToken) => Record("sleep");

    public Task HibernateAsync(CancellationToken cancellationToken) => Record("hibernate");

    public Task LockAsync(CancellationToken cancellationToken) => Record("lock");

    public Task SignOutAsync(CancellationToken cancellationToken) => Record("sign_out");

    private Task Record(string action)
    {
        if (ThrowOnNextAction is { } ex)
        {
            ThrowOnNextAction = null;
            return Task.FromException(ex);
        }

        Actions.Enqueue(action);
        return Task.CompletedTask;
    }
}
