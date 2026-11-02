namespace SmartHomeRelay.Core.Entities;

/// <summary>Home Assistant entity category: where HA shows the entity on the device page.</summary>
public enum EntityCategory
{
    /// <summary>A setting that changes how the device behaves.</summary>
    Config,

    /// <summary>Information about the device itself, e.g. uptime.</summary>
    Diagnostic,
}
