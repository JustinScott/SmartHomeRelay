using System.Collections.Concurrent;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>A module that records sleep/wake notifications, in order, into a shared journal.</summary>
internal sealed class FakePowerAwareModule(string id, ConcurrentQueue<string> journal, params EntityDescriptor[] entities)
    : IFeatureModule, IPowerAware
{
    public string Id { get; } = id;

    public Exception? ThrowOnSuspend { get; set; }

    /// <summary>Runs inside OnSuspendingAsync, e.g. to check what was already published.</summary>
    public Action? OnSuspend { get; set; }

    /// <summary>Runs inside OnResumedAsync.</summary>
    public Action? OnResume { get; set; }

    public IModuleContext? Context { get; private set; }

    public bool IsEnabled(AppSettings settings) => true;

    public IReadOnlyList<EntityDescriptor> DescribeEntities() => entities;

    public Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        Context = context;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OnSuspendingAsync(CancellationToken cancellationToken)
    {
        journal.Enqueue($"{Id} suspending");
        OnSuspend?.Invoke();
        return ThrowOnSuspend is { } ex ? throw ex : Task.CompletedTask;
    }

    public Task OnResumedAsync(CancellationToken cancellationToken)
    {
        journal.Enqueue($"{Id} resumed");
        OnResume?.Invoke();
        return Task.CompletedTask;
    }
}
