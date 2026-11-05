using SmartHomeRelay.Core.Entities;
using Microsoft.Extensions.Logging;

namespace SmartHomeRelay.Core.Features;

/// <summary>What the host gives a running module (spec 3.3).</summary>
public interface IModuleContext
{
    /// <summary>Logger named after the module.</summary>
    ILogger Logger { get; }

    /// <summary>
    /// Publishes an entity's state. Safe to call while disconnected: the latest value per entity is kept
    /// and republished after every (re)connect, so modules never track the connection themselves.
    /// Only keys from the module's own <see cref="IFeatureModule.DescribeEntities"/> are accepted.
    /// </summary>
    Task PublishStateAsync(string entityKey, string payload, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the description of one of the module's entities while it runs (e.g. a select's options when
    /// an audio device is plugged in) and republishes its discovery config. The key and component must stay
    /// the same; anything else is rejected and logged, and the old description stays.
    /// </summary>
    Task UpdateEntityAsync(EntityDescriptor entity, CancellationToken cancellationToken);
}
