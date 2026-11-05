using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>Records every state a module publishes, in order.</summary>
internal sealed class RecordingModuleContext : IModuleContext
{
    public ConcurrentQueue<(string Key, string Payload)> Published { get; } = new();

    public ILogger Logger { get; } = NullLogger.Instance;

    public Task PublishStateAsync(string entityKey, string payload, CancellationToken cancellationToken)
    {
        Published.Enqueue((entityKey, payload));
        return Task.CompletedTask;
    }

    public ConcurrentQueue<EntityDescriptor> Updates { get; } = new();

    public Task UpdateEntityAsync(EntityDescriptor entity, CancellationToken cancellationToken)
    {
        Updates.Enqueue(entity);
        return Task.CompletedTask;
    }

    public ConcurrentQueue<(string Component, string State, string Payload)> CustomPublished { get; } = new();

    public Task PublishCustomStateAsync(string component, string state, string payload, CancellationToken cancellationToken)
    {
        CustomPublished.Enqueue((component, state, payload));
        return Task.CompletedTask;
    }

    /// <summary>The most recent payload of a custom component's state, or null if never published.</summary>
    public string? LatestCustom(string component, string state) =>
        CustomPublished.LastOrDefault(p => p.Component == component && p.State == state).Payload;

    /// <summary>The most recent payload for <paramref name="key"/>, or null if never published.</summary>
    public string? Latest(string key) => Published.LastOrDefault(p => p.Key == key).Payload;

    public int Count(string key) => Published.Count(p => p.Key == key);
}
