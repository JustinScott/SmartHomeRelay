using SmartHomeRelay.Core.Entities;

namespace SmartHomeRelay.Core.Features;

/// <summary>Optional: a module that also describes custom components (spec 3.3).</summary>
public interface IHasCustomComponents
{
    /// <summary>
    /// The module's custom components. Like <see cref="IFeatureModule.DescribeEntities"/>: pure, the same
    /// every time, and also called for disabled modules.
    /// </summary>
    IReadOnlyList<CustomComponent> DescribeCustomComponents();
}
