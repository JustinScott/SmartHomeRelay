namespace SmartHomeRelay.Core.Entities;

/// <summary>How HA shows a number entity (MQTT <c>mode</c>).</summary>
public enum NumberMode
{
    /// <summary>A slider, e.g. volume.</summary>
    Slider,

    /// <summary>An input box.</summary>
    Box,
}
