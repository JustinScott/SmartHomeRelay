namespace SmartHomeRelay.Core.Entities;

/// <summary>Home Assistant MQTT component types used in v1 (spec 3.2, Entities).</summary>
public enum EntityComponent
{
    /// <summary>A read-only value, e.g. CPU usage.</summary>
    Sensor,

    /// <summary>A read-only ON/OFF value, e.g. session locked.</summary>
    BinarySensor,

    /// <summary>A stateless command, e.g. sleep.</summary>
    Button,

    /// <summary>An ON/OFF value that HA can also set, e.g. muted.</summary>
    Switch,

    /// <summary>A numeric value that HA can also set, e.g. volume.</summary>
    Number,

    /// <summary>One of a list of options that HA can also choose, e.g. the output device.</summary>
    Select,

    /// <summary>A target for HA's notify actions; HA sends the message as the command payload.</summary>
    Notify,
}
