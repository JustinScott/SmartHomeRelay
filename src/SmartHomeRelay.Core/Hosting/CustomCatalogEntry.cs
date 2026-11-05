using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features;

namespace SmartHomeRelay.Core.Hosting;

/// <summary>One custom component and the module that owns it.</summary>
/// <param name="Module">The owning module.</param>
/// <param name="Component">The component.</param>
public sealed record CustomCatalogEntry(IFeatureModule Module, CustomComponent Component);
