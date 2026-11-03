namespace SmartHomeRelay.Core.Abstractions;

/// <summary>
/// Access to a user-managed LibreHardwareMonitor on this PC (spec 5.8). Read-only, standard user, and
/// always <c>127.0.0.1</c>: there is no way to point it at another machine. Trust decisions and parsing
/// happen in Core.
/// </summary>
public interface ILibreHardwareMonitorEndpoint
{
    /// <summary>Maximum <c>data.json</c> size accepted.</summary>
    public const int MaxBodyBytes = 4 * 1024 * 1024;

    /// <summary>Time limit for one request.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Looks at who owns <paramref name="port"/> and which LibreHardwareMonitor processes run.</summary>
    LhmEndpointSnapshot Inspect(int port);

    /// <summary>
    /// <c>GET http://127.0.0.1:{port}/data.json</c>, first without credentials, then with them if the server
    /// asks. Never throws for network or HTTP problems: they come back as an outcome.
    /// </summary>
    Task<LhmHttpResult> GetDataAsync(int port, LhmCredentials? credentials, CancellationToken cancellationToken);

    /// <summary>The physical disk number holding the Windows drive (matches LHM's storage IDs), or null.</summary>
    int? GetSystemDiskNumber();
}
