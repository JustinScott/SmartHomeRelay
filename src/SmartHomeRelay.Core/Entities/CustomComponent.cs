namespace SmartHomeRelay.Core.Entities;

/// <summary>
/// A Home Assistant component the standard entity types can't express, such as the <c>media_player</c> of the
/// MQTT Media Player add-on (spec 3.3, 5.9). The module builds the whole discovery config; the host publishes,
/// retains, republishes and clears it, routes its commands, and keeps the latest value of each state.
/// Its topics are <c>{baseTopic}/{deviceId}/{Name}/{state}</c> and <c>{baseTopic}/{deviceId}/{Name}/command/{command}</c>.
/// </summary>
public sealed class CustomComponent
{
    /// <summary>Creates the component.</summary>
    /// <param name="name">Topic level and ID, e.g. <c>media_player</c>. Must not equal any entity key.</param>
    /// <param name="discoveryTopic">The retained config topic for a device ID.</param>
    /// <param name="buildConfig">Builds the config payload (JSON) from the component's topics.</param>
    public CustomComponent(string name, Func<string, string> discoveryTopic, Func<CustomComponentTopics, string> buildConfig)
    {
        Name = name;
        DiscoveryTopic = discoveryTopic;
        BuildConfig = buildConfig;
    }

    /// <summary>Topic level and ID, e.g. <c>media_player</c>. Lowercase letters, digits, and underscores.</summary>
    public string Name { get; }

    /// <summary>The retained discovery config topic for a device ID.</summary>
    public Func<string, string> DiscoveryTopic { get; }

    /// <summary>Builds the discovery config payload.</summary>
    public Func<CustomComponentTopics, string> BuildConfig { get; }

    /// <summary>State names the module may publish (each one is a topic level).</summary>
    public IReadOnlyList<string> States { get; init; } = [];

    /// <summary>Command names the host subscribes to and routes to <see cref="CommandHandler"/>.</summary>
    public IReadOnlyList<string> Commands { get; init; } = [];

    /// <summary>Required when <see cref="Commands"/> is not empty.</summary>
    public CustomCommandHandler? CommandHandler { get; init; }

    /// <summary>Whether states are retained.</summary>
    public bool RetainStates { get; init; } = true;

    /// <summary>
    /// States published while the module is disabled, for components HA won't remove when their config is
    /// cleared (the add-on keeps its entity). An empty payload clears a retained state.
    /// </summary>
    public IReadOnlyDictionary<string, string> DisabledStates { get; init; } = new Dictionary<string, string>();
}
