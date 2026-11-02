namespace SmartHomeRelay.Core.Hosting;

/// <summary>How the tray icon presents a <see cref="RelayStatus"/>.</summary>
public enum StatusSeverity
{
    /// <summary>Normal icon.</summary>
    Ok,

    /// <summary>Amber badge: not connected yet, or retrying.</summary>
    Warning,

    /// <summary>Red badge: the user needs to fix something.</summary>
    Error,
}
