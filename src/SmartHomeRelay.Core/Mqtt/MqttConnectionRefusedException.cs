namespace SmartHomeRelay.Core.Mqtt;

/// <summary>The broker answered but refused the connection (e.g. wrong user name or password).</summary>
public sealed class MqttConnectionRefusedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public MqttConnectionRefusedException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public MqttConnectionRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public MqttConnectionRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a refusal reason reported by the broker.</summary>
    public MqttConnectionRefusedException(string reason, bool isAuthenticationFailure)
        : base($"The MQTT broker refused the connection: {reason}.")
    {
        IsAuthenticationFailure = isAuthenticationFailure;
    }

    /// <summary>
    /// True when the broker rejected the credentials. Retrying won't help until the user name or password
    /// is fixed, so the tray shows an error instead of "Reconnecting".
    /// </summary>
    public bool IsAuthenticationFailure { get; }
}
