namespace SmartHomeRelay.Core.Features.Metrics;

/// <summary>Metrics entity keys (spec 5.5). Shipped keys are permanent: never rename them.</summary>
public static class MetricsKeys
{
    /// <summary>sensor (%): CPU usage.</summary>
    public const string CpuUsage = "cpu_usage";

    /// <summary>sensor (%): physical memory in use.</summary>
    public const string MemoryUsage = "memory_usage";

    /// <summary>sensor (GB): physical memory in use.</summary>
    public const string MemoryUsedGb = "memory_used_gb";

    /// <summary>sensor (Mbit/s): download rate across physical adapters.</summary>
    public const string NetworkDown = "network_down";

    /// <summary>sensor (Mbit/s): upload rate across physical adapters.</summary>
    public const string NetworkUp = "network_up";

    /// <summary>sensor (%): GPU usage (busiest engine). Only if GPU counters exist.</summary>
    public const string GpuUsage = "gpu_usage";

    /// <summary>sensor (%): used space on a drive, e.g. <c>disk_usage_c</c>.</summary>
    public static string DiskUsage(char letter) => $"disk_usage_{char.ToLowerInvariant(letter)}";

    /// <summary>sensor (GB): free space on a drive, e.g. <c>disk_free_c</c>.</summary>
    public static string DiskFree(char letter) => $"disk_free_{char.ToLowerInvariant(letter)}";
}
