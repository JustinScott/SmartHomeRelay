namespace SmartHomeRelay.Core.Configuration;

/// <summary>Identity of this PC in Home Assistant (spec 4.1).</summary>
public sealed class DeviceSettings
{
    /// <summary>
    /// Used in every topic and unique_id. Defaults to the machine host name (same as the prototype)
    /// when left empty. Changing it after entities exist creates new entities in Home Assistant.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name of the Home Assistant device. Defaults to the host name when empty.</summary>
    public string Name { get; set; } = string.Empty;
}
