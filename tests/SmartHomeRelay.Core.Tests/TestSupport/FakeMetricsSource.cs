using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>Settable system counters for metrics tests.</summary>
internal sealed class FakeMetricsSource : ISystemMetricsSource
{
    public const ulong Gb = 1024UL * 1024 * 1024;

    public bool IsGpuAvailable { get; set; } = true;

    public CpuTimes Cpu { get; set; } = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

    public MemoryStatus Memory { get; set; } = new(16 * Gb, 8 * Gb);

    public List<DriveSpace> Drives { get; set; } = [new('C', 500 * Gb, 250 * Gb), new('D', 1000 * Gb, 900 * Gb)];

    public List<NetworkAdapterCounters> Adapters { get; set; } = [new("eth0", "Intel(R) Ethernet", NetworkAdapterKind.Ethernet, true, 0, 0)];

    public Dictionary<string, double> GpuEngines { get; set; } = new() { ["pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D"] = 10 };

    public Exception? ThrowOnGpu { get; set; }

    public int GpuReads { get; private set; }

    /// <summary>Simulates CPU activity: <paramref name="busyPercent"/> busy over <paramref name="elapsed"/> on one core.</summary>
    public void RunCpu(TimeSpan elapsed, double busyPercent)
    {
        var busy = TimeSpan.FromTicks((long)(elapsed.Ticks * busyPercent / 100));
        var idle = elapsed - busy;
        Cpu = new CpuTimes(Cpu.Idle + idle, Cpu.Kernel + idle, Cpu.User + busy);
    }

    /// <summary>Adds traffic to the first adapter.</summary>
    public void Transfer(long bytesReceived, long bytesSent)
    {
        var a = Adapters[0];
        Adapters[0] = a with { BytesReceived = a.BytesReceived + bytesReceived, BytesSent = a.BytesSent + bytesSent };
    }

    public CpuTimes GetCpuTimes() => Cpu;

    public MemoryStatus GetMemory() => Memory;

    public IReadOnlyList<DriveSpace> GetFixedDrives() => [.. Drives];

    public IReadOnlyList<NetworkAdapterCounters> GetNetworkAdapters() => [.. Adapters];

    public IReadOnlyDictionary<string, double> GetGpuEngineUtilization()
    {
        GpuReads++;
        if (ThrowOnGpu is { } ex)
        {
            throw ex;
        }

        return new Dictionary<string, double>(GpuEngines);
    }
}
