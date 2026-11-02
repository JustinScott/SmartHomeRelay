using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features;

namespace SmartHomeRelay.Core.Hosting;

/// <summary>One entity and the module that owns it.</summary>
/// <param name="Module">The owning module.</param>
/// <param name="Entity">The entity.</param>
public sealed record CatalogEntry(IFeatureModule Module, EntityDescriptor Entity);
