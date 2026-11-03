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

    /// <summary>Like BOX1 (Ryzen): per-core and package RAPL instances plus a <c>_total</c> that reads 0.</summary>
    public List<string> EnergyMeterInstances { get; set; } = ["rapl_package0_core0_core", "rapl_package0_core1_core", "rapl_package0_pkg", "_total"];

    public Dictionary<string, double> EnergyMeterMilliwatts { get; set; } = new()
    {
        ["rapl_package0_core0_core"] = 930,
        ["rapl_package0_core1_core"] = 3350,
        ["rapl_package0_pkg"] = 56_050,
        ["_total"] = 0,
    };

    /// <summary>Like BOX1: one AMD card that Windows lists under two adapter IDs.</summary>
    public List<GpuAdapterSensors> GpuSensors { get; set; } = [new(0x4160BFB, 47.0, 3.2, 1095), new(0x22414, 47.0, 3.2, 1095)];

    public Exception? ThrowOnGpuSensors { get; set; }

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

    public IReadOnlyList<string> GetEnergyMeterInstances() => [.. EnergyMeterInstances];

    public IReadOnlyDictionary<string, double> GetEnergyMeterPowerMilliwatts() => new Dictionary<string, double>(EnergyMeterMilliwatts);

    public IReadOnlyList<GpuAdapterSensors> GetGpuAdapterSensors()
    {
        if (ThrowOnGpuSensors is { } ex)
        {
            throw ex;
        }

        return [.. GpuSensors];
    }
}
