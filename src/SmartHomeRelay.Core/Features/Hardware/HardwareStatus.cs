namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>States of the <c>hardware_status</c> sensor (spec 5.8). The published values are the lowercase names.</summary>
public enum HardwareStatus
{
    /// <summary>A trusted, tested LibreHardwareMonitor is being read.</summary>
    Ok,

    /// <summary>LibreHardwareMonitor is newer than the tested version; its data passed validation.</summary>
    NewerThanTested,

    /// <summary>LibreHardwareMonitor isn't running, or its web server is off.</summary>
    NotRunning,

    /// <summary>The web server refused the configured login, or needs one that isn't set.</summary>
    LoginFailed,

    /// <summary>The port's owner or LibreHardwareMonitor's install location failed the anti-spoofing checks.</summary>
    Untrusted,

    /// <summary>LibreHardwareMonitor is too old, or its data format isn't understood.</summary>
    Incompatible,
}
