namespace SmartHomeRelay.Core.Mqtt;

/// <summary>MQTT quality of service levels used by the app.</summary>
public enum MqttQos
{
    /// <summary>QoS 0: fire and forget. Used for state updates, which are republished on reconnect.</summary>
    AtMostOnce = 0,

    /// <summary>QoS 1: used for availability, discovery, and commands (spec 4.3).</summary>
    AtLeastOnce = 1,
}
