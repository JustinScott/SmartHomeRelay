using System.Collections.Concurrent;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>A configurable module for host tests.</summary>
internal sealed class FakeModule(string id, params EntityDescriptor[] entities) : IFeatureModule
{
    public string Id { get; } = id;

    public Func<AppSettings, bool> Enabled { get; set; } = _ => true;

    public Exception? ThrowOnStart { get; set; }

    public Exception? ThrowOnDescribe { get; set; }

    public IModuleContext? Context { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public bool IsEnabled(AppSettings settings) => Enabled(settings);

    public IReadOnlyList<EntityDescriptor> DescribeEntities() => ThrowOnDescribe is { } ex ? throw ex : entities;

    public Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        StartCount++;
        if (ThrowOnStart is { } ex)
        {
            throw ex;
        }

        Context = context;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopCount++;
        return Task.CompletedTask;
    }

    public static EntityDescriptor Sensor(string key) => new(key, key.Replace('_', ' '), EntityComponent.Sensor);

    /// <summary>A button whose presses are recorded in <paramref name="presses"/>.</summary>
    public static EntityDescriptor Button(string key, ConcurrentQueue<string> presses) =>
        new(key, key.Replace('_', ' '), EntityComponent.Button)
        {
            CommandHandler = (payload, _) =>
            {
                presses.Enqueue(payload);
                return Task.CompletedTask;
            },
        };
}
