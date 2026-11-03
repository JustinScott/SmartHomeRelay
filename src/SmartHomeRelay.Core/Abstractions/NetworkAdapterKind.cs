namespace SmartHomeRelay.Core.Abstractions;

/// <summary>The kinds of network adapter the metrics logic distinguishes.</summary>
public enum NetworkAdapterKind
{
    /// <summary>Wired Ethernet (any speed).</summary>
    Ethernet,

    /// <summary>Wi-Fi.</summary>
    Wireless,

    /// <summary>Loopback.</summary>
    Loopback,

    /// <summary>Tunnels (VPNs, IPv6 transition).</summary>
    Tunnel,

    /// <summary>Anything else.</summary>
    Other,
}
