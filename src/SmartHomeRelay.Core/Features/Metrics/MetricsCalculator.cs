using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Features.Metrics;

/// <summary>Pure metric math (spec 5.5): CPU %, memory, network rates, GPU aggregation, disk.</summary>
public static class MetricsCalculator
{
    /// <summary>Bytes per gigabyte (GiB, as Windows Task Manager and Explorer show "GB").</summary>
    public const double BytesPerGigabyte = 1024d * 1024 * 1024;

    private static readonly string[] s_virtualAdapterMarkers =
    [
        "virtual", "hyper-v", "vmware", "virtualbox", "loopback", "wan miniport", "pseudo", "bluetooth", "wsl",
    ];

    /// <summary>
    /// CPU usage between two samples: busy time / total time. Null if the samples can't be compared
    /// (no time passed, or counters went backwards).
    /// </summary>
    public static double? CpuUsagePercent(CpuTimes previous, CpuTimes current)
    {
        var idle = current.Idle - previous.Idle;
        var total = (current.Kernel - previous.Kernel) + (current.User - previous.User);
        if (total <= TimeSpan.Zero || idle < TimeSpan.Zero || idle > total)
        {
            return null;
        }

        return Math.Clamp(100.0 * (total - idle).Ticks / total.Ticks, 0, 100);
    }

    /// <summary>Percentage of physical memory in use.</summary>
    public static double MemoryUsagePercent(MemoryStatus memory) =>
        memory.TotalBytes == 0 ? 0 : 100.0 * UsedBytes(memory) / memory.TotalBytes;

    /// <summary>Physical memory in use, in GB.</summary>
    public static double MemoryUsedGigabytes(MemoryStatus memory) => UsedBytes(memory) / BytesPerGigabyte;

    /// <summary>Percentage of a drive in use.</summary>
    public static double DiskUsagePercent(DriveSpace drive) =>
        drive.TotalBytes == 0 ? 0 : 100.0 * (drive.TotalBytes - Math.Min(drive.FreeBytes, drive.TotalBytes)) / drive.TotalBytes;

    /// <summary>Free space on a drive, in GB.</summary>
    public static double DiskFreeGigabytes(DriveSpace drive) => drive.FreeBytes / BytesPerGigabyte;

    /// <summary>
    /// Whether an adapter counts toward the network rates: physical Ethernet or Wi-Fi that is up. Virtual,
    /// VPN, and loopback adapters are left out because their traffic also crosses a physical adapter and
    /// would be counted twice.
    /// </summary>
    public static bool IsPhysicalAdapter(NetworkAdapterCounters adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (!adapter.IsUp || adapter.Kind is not (NetworkAdapterKind.Ethernet or NetworkAdapterKind.Wireless))
        {
            return false;
        }

        var description = adapter.Description.ToLowerInvariant();
        return !s_virtualAdapterMarkers.Any(description.Contains);
    }

    /// <summary>
    /// Download and upload rates in Mbit/s between two samples, over physical adapters present in both.
    /// An adapter whose counters went backwards (reset after sleep or reconnect) is skipped.
    /// </summary>
    public static (double DownMbps, double UpMbps) NetworkRates(
        IReadOnlyList<NetworkAdapterCounters> previous, IReadOnlyList<NetworkAdapterCounters> current, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (elapsed <= TimeSpan.Zero)
        {
            return (0, 0);
        }

        var before = previous.Where(IsPhysicalAdapter).ToDictionary(a => a.Id, StringComparer.Ordinal);
        long received = 0;
        long sent = 0;
        foreach (var adapter in current.Where(IsPhysicalAdapter))
        {
            if (!before.TryGetValue(adapter.Id, out var old)
                || adapter.BytesReceived < old.BytesReceived
                || adapter.BytesSent < old.BytesSent)
            {
                continue;
            }

            received += adapter.BytesReceived - old.BytesReceived;
            sent += adapter.BytesSent - old.BytesSent;
        }

        return (ToMbps(received, elapsed), ToMbps(sent, elapsed));
    }

    /// <summary>
    /// GPU usage like Task Manager: utilization summed per engine (across processes), then the busiest
    /// engine of any GPU. Instance names look like <c>pid_1234_luid_0x0_0x1A2B_phys_0_eng_3_engtype_3D</c>.
    /// </summary>
    public static double? GpuUsagePercent(IReadOnlyDictionary<string, double> engineUtilization)
    {
        ArgumentNullException.ThrowIfNull(engineUtilization);
        if (engineUtilization.Count == 0)
        {
            return null;
        }

        var perEngine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, utilization) in engineUtilization)
        {
            var engine = EngineKey(instance);
            perEngine[engine] = perEngine.GetValueOrDefault(engine) + Math.Max(0, utilization);
        }

        return Math.Clamp(perEngine.Values.Max(), 0, 100);
    }

    /// <summary>The engine part of an instance name: everything from <c>luid_</c> on (drops the process ID).</summary>
    internal static string EngineKey(string instanceName)
    {
        var luid = instanceName.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        return luid >= 0 ? instanceName[luid..] : instanceName;
    }

    private static ulong UsedBytes(MemoryStatus memory) => memory.TotalBytes - Math.Min(memory.AvailableBytes, memory.TotalBytes);

    private static double ToMbps(long bytes, TimeSpan elapsed) => bytes * 8 / 1_000_000.0 / elapsed.TotalSeconds;
}
