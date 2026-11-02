namespace SmartHomeRelay.Core.Entities;

/// <summary>
/// Describes one entity a module exposes to Home Assistant. The <see cref="Key"/> is a compatibility
/// contract: it appears in topics and unique IDs, so a shipped key must never be renamed (spec 13).
/// </summary>
/// <param name="Key">Stable entity key: lowercase letters, digits, and underscores, e.g. <c>cpu_usage</c>.</param>
/// <param name="Name">Display name in HA, e.g. <c>CPU usage</c>.</param>
/// <param name="Component">The HA component type.</param>
public sealed record EntityDescriptor(string Key, string Name, EntityComponent Component)
{
    /// <summary>HA device class, e.g. <c>duration</c> or <c>timestamp</c>.</summary>
    public string? DeviceClass { get; init; }

    /// <summary>Unit, e.g. <c>%</c> or <c>s</c>.</summary>
    public string? UnitOfMeasurement { get; init; }

    /// <summary>HA state class, e.g. <c>measurement</c>.</summary>
    public string? StateClass { get; init; }

    /// <summary>Material Design icon, e.g. <c>mdi:monitor</c>.</summary>
    public string? Icon { get; init; }

    /// <summary>Where HA shows the entity; null for a primary entity.</summary>
    public EntityCategory? Category { get; init; }

    /// <summary>Allowed values for an enum sensor (device class <c>enum</c>).</summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>Range for a <see cref="EntityComponent.Number"/>. Required for numbers.</summary>
    public NumberRange? Range { get; init; }

    /// <summary>Whether state messages are retained (sensors yes; spec 4.3).</summary>
    public bool RetainState { get; init; } = true;

    /// <summary>Required for buttons, switches, and numbers; must be null otherwise.</summary>
    public EntityCommandHandler? CommandHandler { get; init; }

    /// <summary>True for component types that receive commands from HA.</summary>
    public bool AcceptsCommands => Component is EntityComponent.Button or EntityComponent.Switch or EntityComponent.Number;

    /// <summary>True for component types that have a state topic.</summary>
    public bool HasState => Component != EntityComponent.Button;
}
