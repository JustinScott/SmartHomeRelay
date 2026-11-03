namespace SmartHomeRelay.Core.Abstractions;

/// <summary>
/// Raw system counters for the Metrics module (spec 5.5). Returns cumulative counters, not rates or
/// percentages, so all the math lives in Core where it is testable. Nothing here needs elevation.
/// </summary>
public interface ISystemMetricsSource
{
    /// <summary>True if the Windows GPU Engine performance counters exist (spec 5.5: no GPU entity otherwise).</summary>
    bool IsGpuAvailable { get; }

    /// <summary>Cumulative CPU times.</summary>
    CpuTimes GetCpuTimes();

    /// <summary>Physical memory status.</summary>
    MemoryStatus GetMemory();

    /// <summary>Fixed drives that are ready, sorted by letter.</summary>
    IReadOnlyList<DriveSpace> GetFixedDrives();

    /// <summary>All network adapters with their cumulative byte counters.</summary>
    IReadOnlyList<NetworkAdapterCounters> GetNetworkAdapters();

    /// <summary>
    /// Utilization (0-100) per GPU engine instance since the previous call, keyed by the counter instance
    /// name (e.g. <c>pid_1234_luid_0x0_0x1A2B_phys_0_eng_0_engtype_3D</c>). Empty on the first call or if
    /// unavailable.
    /// </summary>
    IReadOnlyDictionary<string, double> GetGpuEngineUtilization();

    /// <summary>
    /// Instance names of the Windows "Energy Meter" performance counters (e.g. <c>rapl_package0_pkg</c>),
    /// which expose the CPU's own energy counters. Empty if the PC doesn't have them.
    /// </summary>
    IReadOnlyList<string> GetEnergyMeterInstances();

    /// <summary>
    /// Power in milliwatts per "Energy Meter" instance since the previous call. Empty on the first call or
    /// if unavailable.
    /// </summary>
    IReadOnlyDictionary<string, double> GetEnergyMeterPowerMilliwatts();

    /// <summary>Per-adapter GPU sensors from Windows (temperature, power level, fan). Empty if unavailable.</summary>
    IReadOnlyList<GpuAdapterSensors> GetGpuAdapterSensors();
}
