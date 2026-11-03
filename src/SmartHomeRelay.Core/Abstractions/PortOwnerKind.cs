namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Who is listening on a local TCP port.</summary>
public enum PortOwnerKind
{
    /// <summary>Nothing is listening.</summary>
    None,

    /// <summary>
    /// The System process: Windows' HTTP.sys, which LibreHardwareMonitor's web server uses. Registering an
    /// HTTP.sys listener on a port needs admin rights or an admin-made URL reservation.
    /// </summary>
    HttpSys,

    /// <summary>An ordinary process; see <see cref="LhmEndpointSnapshot.OwnerImagePath"/>.</summary>
    Process,

    /// <summary>Something is listening but its owner couldn't be determined.</summary>
    Unknown,
}
