using System.Collections.Concurrent;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>A module with one custom component ("gadget": states "state" and "title", command "go").</summary>
internal sealed class FakeCustomModule(string id, string componentName = "gadget", params EntityDescriptor[] entities) : IFeatureModule, IHasCustomComponents
{
    public string Id { get; } = id;

    public Func<AppSettings, bool> Enabled { get; set; } = _ => true;

    public IModuleContext? Context { get; private set; }

    public ConcurrentQueue<(string Command, string Payload)> Commands { get; } = new();

    public bool IsEnabled(AppSettings settings) => Enabled(settings);

    public IReadOnlyList<EntityDescriptor> DescribeEntities() => entities;

    public IReadOnlyList<CustomComponent> DescribeCustomComponents() =>
    [
        new(componentName, deviceId => $"homeassistant/gadget/{deviceId}/config", t => $"{{\"state\":\"{t.State("state")}\",\"go\":\"{t.Command("go")}\"}}")
        {
            States = ["state", "title"],
            Commands = ["go"],
            CommandHandler = (command, payload, _) =>
            {
                Commands.Enqueue((command, payload));
                return Task.CompletedTask;
            },
            DisabledStates = new Dictionary<string, string> { ["state"] = "off", ["title"] = string.Empty },
        },
    ];

    public Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        Context = context;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
