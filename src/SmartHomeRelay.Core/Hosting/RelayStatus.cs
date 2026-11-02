namespace SmartHomeRelay.Core.Hosting;

/// <summary>Overall state shown in the tray (spec 11).</summary>
public enum RelayStatus
{
    /// <summary>No broker host is set.</summary>
    NotConfigured,

    /// <summary>The settings file has errors; see the log.</summary>
    SettingsInvalid,

    /// <summary>Configured, but no connection attempt has been made yet.</summary>
    Disconnected,

    /// <summary>First connection attempt in progress.</summary>
    Connecting,

    /// <summary>Connected to the broker.</summary>
    Connected,

    /// <summary>The connection failed or dropped; retrying with backoff.</summary>
    Reconnecting,

    /// <summary>Cannot connect until the user fixes something, e.g. the password cannot be decrypted.</summary>
    Error,
}
