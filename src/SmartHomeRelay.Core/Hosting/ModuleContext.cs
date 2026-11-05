using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features;

namespace SmartHomeRelay.Core.Hosting;

/// <summary>The <see cref="IModuleContext"/> given to one module.</summary>
internal sealed class ModuleContext(ModuleHost host, IFeatureModule module, ILogger logger) : IModuleContext
{
    public ILogger Logger { get; } = logger;

    public Task PublishStateAsync(string entityKey, string payload, CancellationToken cancellationToken) =>
        host.PublishStateAsync(module, entityKey, payload, cancellationToken);

    public Task UpdateEntityAsync(EntityDescriptor entity, CancellationToken cancellationToken) =>
        host.UpdateEntityAsync(module, entity, cancellationToken);
}
