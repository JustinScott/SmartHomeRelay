using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Interop;

namespace SmartHomeRelay.Platform.Windows.Metrics;

/// <summary>
/// Raw counters for the Metrics module (spec 5.5), all readable by a standard user (no driver, no
/// elevation). Thin OS wrapper: percentages, rates, and which instance counts are decided in Core.
/// </summary>
public sealed class WindowsSystemMetricsSource : ISystemMetricsSource
{
    private readonly PerformanceCounterDeltaReader _gpuEngines = new("GPU Engine", "Utilization Percentage");
    private readonly PerformanceCounterDeltaReader _energyMeter = new("Energy Meter", "Power");

    /// <inheritdoc />
    public bool IsGpuAvailable => _gpuEngines.Exists;

    /// <inheritdoc />
    public CpuTimes GetCpuTimes()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        // FILETIME durations are in 100 ns units, the same as TimeSpan ticks.
        return new CpuTimes(TimeSpan.FromTicks(idle), TimeSpan.FromTicks(kernel), TimeSpan.FromTicks(user));
    }

    /// <inheritdoc />
    public MemoryStatus GetMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return new MemoryStatus(status.TotalPhys, status.AvailPhys);
    }

    /// <inheritdoc />
    public IReadOnlyList<DriveSpace> GetFixedDrives() =>
        [.. DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady && d.Name.Length > 0 && char.IsAsciiLetter(d.Name[0]))
            .Select(d => new DriveSpace(char.ToUpperInvariant(d.Name[0]), (ulong)d.TotalSize, (ulong)d.AvailableFreeSpace))
            .OrderBy(d => d.Letter)];

    /// <inheritdoc />
    public IReadOnlyList<NetworkAdapterCounters> GetNetworkAdapters()
    {
        var adapters = new List<NetworkAdapterCounters>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var stats = adapter.GetIPStatistics();
                adapters.Add(new NetworkAdapterCounters(
                    adapter.Id,
                    adapter.Description,
                    Map(adapter.NetworkInterfaceType),
                    adapter.OperationalStatus == OperationalStatus.Up,
                    stats.BytesReceived,
                    stats.BytesSent));
            }
            catch (NetworkInformationException)
            {
                // Some virtual adapters don't expose statistics; they wouldn't be counted anyway.
            }
        }

        return adapters;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Clamped to 0-100: Windows' GPU Engine counter can report more than 100% for one instance (499% seen on a CI
    /// VM between two reads milliseconds apart), and the interface promises percentages.
    /// </remarks>
    public IReadOnlyDictionary<string, double> GetGpuEngineUtilization() =>
        _gpuEngines.Read().ToDictionary(e => e.Key, e => Math.Clamp(e.Value, 0, 100), StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<string> GetEnergyMeterInstances() => _energyMeter.GetInstanceNames();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, double> GetEnergyMeterPowerMilliwatts() => _energyMeter.Read();

    /// <inheritdoc />
    public IReadOnlyList<GpuAdapterSensors> GetGpuAdapterSensors()
    {
        // First call gets the count, second fills the buffer (and opens a handle per adapter).
        var enumAdapters = default(D3dkmtEnumAdapters2);
        if (NativeMethods.D3DKMTEnumAdapters2(ref enumAdapters) != 0 || enumAdapters.NumAdapters == 0)
        {
            return [];
        }

        var size = Marshal.SizeOf<D3dkmtAdapterInfo>();
        var buffer = Marshal.AllocHGlobal(size * (int)enumAdapters.NumAdapters);
        try
        {
            enumAdapters.Adapters = buffer;

            // Fails (and opens nothing) if an adapter appeared in between; this sample is then skipped.
            if (NativeMethods.D3DKMTEnumAdapters2(ref enumAdapters) != 0)
            {
                return [];
            }

            var sensors = new List<GpuAdapterSensors>();
            for (var i = 0; i < enumAdapters.NumAdapters; i++)
            {
                var info = Marshal.PtrToStructure<D3dkmtAdapterInfo>(buffer + (i * size));
                try
                {
                    if (TryQueryPerfData(info.Adapter, out var perf))
                    {
                        sensors.Add(ToSensors(info, perf));
                    }
                }
                finally
                {
                    var close = new D3dkmtCloseAdapter { Adapter = info.Adapter };
                    _ = NativeMethods.D3DKMTCloseAdapter(ref close);
                }
            }

            return sensors;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Converts the driver's tenths of a degree and tenths of a percent.</summary>
    internal static GpuAdapterSensors ToSensors(D3dkmtAdapterInfo info, D3dkmtAdapterPerfData perf) =>
        new(
            ((long)info.LuidHighPart << 32) | info.LuidLowPart,
            perf.Temperature / 10.0,
            perf.Power / 10.0,
            perf.FanRpm);

    private static unsafe bool TryQueryPerfData(uint adapter, out D3dkmtAdapterPerfData perf)
    {
        perf = default;
        fixed (D3dkmtAdapterPerfData* data = &perf)
        {
            var query = new D3dkmtQueryAdapterInfo
            {
                Adapter = adapter,
                Type = NativeMethods.KmtqaitypeAdapterPerfData,
                PrivateDriverData = (nint)data,
                PrivateDriverDataSize = (uint)sizeof(D3dkmtAdapterPerfData),
            };

            // Software adapters (Microsoft Basic Render Driver) return STATUS_INVALID_PARAMETER: no sensors.
            return NativeMethods.D3DKMTQueryAdapterInfo(ref query) == 0;
        }
    }

    internal static NetworkAdapterKind Map(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => NetworkAdapterKind.Ethernet,
        NetworkInterfaceType.Wireless80211 => NetworkAdapterKind.Wireless,
        NetworkInterfaceType.Loopback => NetworkAdapterKind.Loopback,
        NetworkInterfaceType.Tunnel => NetworkAdapterKind.Tunnel,
        _ => NetworkAdapterKind.Other,
    };
}
