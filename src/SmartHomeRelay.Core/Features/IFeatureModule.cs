using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;

namespace SmartHomeRelay.Core.Features;

/// <summary>
/// A self-contained capability (spec 3.3). Modules never reference each other; the host runs each one
/// in isolation, so a failing module cannot affect the others.
/// </summary>
public interface IFeatureModule
{
    /// <summary>Stable module ID, e.g. <c>power</c>.</summary>
    string Id { get; }

    /// <summary>Whether the module should run with these settings.</summary>
    bool IsEnabled(AppSettings settings);

    /// <summary>
    /// The entities this module exposes. Must be pure (no I/O) and return the same keys every time:
    /// it is also called for disabled modules so their discovery configs can be cleared.
    /// </summary>
    IReadOnlyList<EntityDescriptor> DescribeEntities();

    /// <summary>Starts the module. Called once when it becomes enabled, not on every reconnect.</summary>
    Task StartAsync(IModuleContext context, CancellationToken cancellationToken);

    /// <summary>Stops the module and releases its resources.</summary>
    Task StopAsync(CancellationToken cancellationToken);
}
