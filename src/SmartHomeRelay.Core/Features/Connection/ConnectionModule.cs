using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;

namespace SmartHomeRelay.Core.Features.Connection;

/// <summary>
/// Connected (spec 4.3): a binary sensor that is on while Smart Home Relay is connected and off while the PC is
/// offline (asleep, shut down, or the app closed), for automations such as "the PC went off". It reads the
/// availability topic, so there is nothing to publish and it is always on.
/// </summary>
public sealed class ConnectionModule : IFeatureModule
{
    /// <summary>The entity key. Never rename it.</summary>
    public const string ConnectedKey = "connected";

    private static readonly IReadOnlyList<EntityDescriptor> s_entities =
    [
        new(ConnectedKey, "Connected", EntityComponent.BinarySensor)
        {
            DeviceClass = "connectivity",
            ReportsConnection = true,
        },
    ];

    /// <inheritdoc />
    public string Id => "connection";

    /// <inheritdoc />
    public bool IsEnabled(AppSettings settings) => true;

    /// <inheritdoc />
    public IReadOnlyList<EntityDescriptor> DescribeEntities() => s_entities;

    /// <inheritdoc />
    public Task StartAsync(IModuleContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
