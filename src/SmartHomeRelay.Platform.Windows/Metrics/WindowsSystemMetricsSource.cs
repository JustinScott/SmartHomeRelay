using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Interop;

namespace SmartHomeRelay.Platform.Windows.Metrics;

/// <summary>
/// Raw counters for the Metrics module (spec 5.5), all readable by a standard user. Thin OS wrapper:
/// percentages and rates are computed in Core.
/// </summary>
public sealed class WindowsSystemMetricsSource : ISystemMetricsSource
{
    private const string GpuCategory = "GPU Engine";
    private const string GpuCounter = "Utilization Percentage";

    private readonly Lock _gpuGate = new();
    private Dictionary<string, CounterSample> _lastGpuSamples = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsGpuAvailable
    {
        get
        {
            try
            {
                return PerformanceCounterCategory.Exists(GpuCategory)
                    && PerformanceCounterCategory.CounterExists(GpuCounter, GpuCategory);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

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
    public IReadOnlyDictionary<string, double> GetGpuEngineUtilization()
    {
        lock (_gpuGate)
        {
            InstanceDataCollection? instances;
            try
            {
                // One read of the whole category per sample: far cheaper than a counter per engine instance,
                // and instances come and go with processes.
                instances = new PerformanceCounterCategory(GpuCategory).ReadCategory()[GpuCounter];
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
            {
                return new Dictionary<string, double>();
            }

            var current = new Dictionary<string, CounterSample>(StringComparer.Ordinal);
            var utilization = new Dictionary<string, double>(StringComparer.Ordinal);
            if (instances is not null)
            {
                foreach (InstanceData instance in instances.Values)
                {
                    current[instance.InstanceName] = instance.Sample;
                    if (_lastGpuSamples.TryGetValue(instance.InstanceName, out var previous))
                    {
                        // Utilization is a time-based counter: it needs two samples to give a percentage.
                        utilization[instance.InstanceName] = CounterSample.Calculate(previous, instance.Sample);
                    }
                }
            }

            _lastGpuSamples = current;
            return utilization;
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
