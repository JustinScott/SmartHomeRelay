using SmartHomeRelay.Core.Mqtt;

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

    /// <summary>Allowed values for an enum sensor (device class <c>enum</c>) or a <see cref="EntityComponent.Select"/>.</summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>Range for a <see cref="EntityComponent.Number"/>. Required for numbers.</summary>
    public NumberRange? Range { get; init; }

    /// <summary>How HA shows a <see cref="EntityComponent.Number"/>; null lets HA decide.</summary>
    public NumberMode? Mode { get; init; }

    /// <summary>Optional extra availability condition; null means available whenever the device is online.</summary>
    public EntityAvailability? AvailableWhen { get; init; }

    /// <summary>
    /// A binary sensor whose state is the device's own availability topic (<c>online</c>/<c>offline</c>), with no
    /// availability of its own, so HA shows it <c>off</c> rather than unavailable while the PC is offline (spec 4.3).
    /// Its state is never published by a module.
    /// </summary>
    public bool ReportsConnection { get; init; }

    /// <summary>Whether state messages are retained (sensors yes; spec 4.3).</summary>
    public bool RetainState { get; init; } = true;

    /// <summary>Required for buttons, switches, numbers, and selects; must be null otherwise.</summary>
    public EntityCommandHandler? CommandHandler { get; init; }

    /// <summary>
    /// A command topic other than the usual <c>{key}/set</c>, for a topic that is already a contract (the
    /// notification topic, spec 5.6). Null for the usual one. Only for entities that accept commands.
    /// </summary>
    public Func<MqttTopics, string>? CommandTopic { get; init; }

    /// <summary>True for component types that receive commands from HA.</summary>
    public bool AcceptsCommands => Component is EntityComponent.Button or EntityComponent.Switch or EntityComponent.Number or EntityComponent.Select or EntityComponent.Notify;

    /// <summary>True for component types that have a state topic.</summary>
    public bool HasState => Component is not (EntityComponent.Button or EntityComponent.Notify);
}
