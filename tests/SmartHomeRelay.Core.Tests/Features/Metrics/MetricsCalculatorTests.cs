using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Features.Metrics;

namespace SmartHomeRelay.Core.Tests.Features.Metrics;

public sealed class MetricsCalculatorTests
{
    private const ulong Gb = 1024UL * 1024 * 1024;

    [Fact]
    public void CpuUsage_HalfBusy_IsFiftyPercent()
    {
        // 10 s total across all cores: 5 s idle (inside kernel), 1 s other kernel, 4 s user.
        var previous = new CpuTimes(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        var current = new CpuTimes(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4));

        MetricsCalculator.CpuUsagePercent(previous, current)!.Value.ShouldBe(50, tolerance: 0.001);
    }

    [Fact]
    public void CpuUsage_FullyIdle_IsZero()
    {
        var current = new CpuTimes(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.Zero);

        MetricsCalculator.CpuUsagePercent(default, current).ShouldBe(0);
    }

    [Fact]
    public void CpuUsage_NoTimePassedOrCountersWentBackwards_IsNull()
    {
        var sample = new CpuTimes(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4));

        MetricsCalculator.CpuUsagePercent(sample, sample).ShouldBeNull();
        MetricsCalculator.CpuUsagePercent(sample, default).ShouldBeNull();
    }

    [Fact]
    public void Memory_UsedIsTotalMinusAvailable()
    {
        var memory = new MemoryStatus(16 * Gb, 4 * Gb);

        MetricsCalculator.MemoryUsagePercent(memory).ShouldBe(75);
        MetricsCalculator.MemoryUsedGigabytes(memory).ShouldBe(12);
    }

    [Fact]
    public void Disk_UsageAndFree()
    {
        var drive = new DriveSpace('C', 500 * Gb, 125 * Gb);

        MetricsCalculator.DiskUsagePercent(drive).ShouldBe(75);
        MetricsCalculator.DiskFreeGigabytes(drive).ShouldBe(125);
    }

    [Fact]
    public void Disk_ZeroSize_DoesNotDivideByZero()
    {
        MetricsCalculator.DiskUsagePercent(new DriveSpace('C', 0, 0)).ShouldBe(0);
    }

    [Theory]
    [InlineData(NetworkAdapterKind.Ethernet, true, "Intel(R) Ethernet Controller I225-V", true)]
    [InlineData(NetworkAdapterKind.Wireless, true, "Intel(R) Wi-Fi 6E AX211", true)]
    [InlineData(NetworkAdapterKind.Ethernet, false, "Intel(R) Ethernet Controller I225-V", false)] // down
    [InlineData(NetworkAdapterKind.Ethernet, true, "Hyper-V Virtual Ethernet Adapter", false)]
    [InlineData(NetworkAdapterKind.Ethernet, true, "VMware Virtual Ethernet Adapter for VMnet8", false)]
    [InlineData(NetworkAdapterKind.Ethernet, true, "VirtualBox Host-Only Ethernet Adapter", false)]
    [InlineData(NetworkAdapterKind.Ethernet, true, "Bluetooth Device (Personal Area Network)", false)]
    [InlineData(NetworkAdapterKind.Loopback, true, "Software Loopback Interface 1", false)]
    [InlineData(NetworkAdapterKind.Tunnel, true, "WireGuard Tunnel", false)]
    [InlineData(NetworkAdapterKind.Other, true, "TAP-Windows Adapter V9", false)]
    public void IsPhysicalAdapter_CountsOnlyRealEthernetAndWifi(NetworkAdapterKind kind, bool up, string description, bool expected)
    {
        MetricsCalculator.IsPhysicalAdapter(new NetworkAdapterCounters("id", description, kind, up, 0, 0)).ShouldBe(expected);
    }

    [Fact]
    public void NetworkRates_ComputesMegabitsPerSecond()
    {
        NetworkAdapterCounters[] before = [Adapter("eth", 0, 0)];
        NetworkAdapterCounters[] after = [Adapter("eth", 10_000_000, 1_000_000)]; // 10 MB down, 1 MB up

        var (down, up) = MetricsCalculator.NetworkRates(before, after, TimeSpan.FromSeconds(10));

        down.ShouldBe(8, tolerance: 0.0001); // 80 Mbit over 10 s
        up.ShouldBe(0.8, tolerance: 0.0001);
    }

    [Fact]
    public void NetworkRates_SumsPhysicalAdaptersAndIgnoresVirtualOnes()
    {
        NetworkAdapterCounters[] before = [Adapter("eth", 0, 0), Adapter("wifi", 0, 0), Adapter("vm", 0, 0, "Hyper-V Virtual Ethernet Adapter")];
        NetworkAdapterCounters[] after = [Adapter("eth", 1_250_000, 0), Adapter("wifi", 1_250_000, 0), Adapter("vm", 99_000_000, 0, "Hyper-V Virtual Ethernet Adapter")];

        MetricsCalculator.NetworkRates(before, after, TimeSpan.FromSeconds(1)).DownMbps.ShouldBe(20, tolerance: 0.0001);
    }

    [Fact]
    public void NetworkRates_CounterResetOrNewAdapter_IsSkippedNotNegative()
    {
        NetworkAdapterCounters[] before = [Adapter("eth", 5_000_000, 5_000_000)];
        NetworkAdapterCounters[] after = [Adapter("eth", 100, 100), Adapter("new", 9_000_000, 0)];

        MetricsCalculator.NetworkRates(before, after, TimeSpan.FromSeconds(1)).ShouldBe((0, 0));
    }

    [Fact]
    public void GpuUsage_SumsProcessesPerEngineThenTakesBusiestEngine()
    {
        var engines = new Dictionary<string, double>
        {
            ["pid_10_luid_0x0_0x1_phys_0_eng_0_engtype_3D"] = 30,
            ["pid_20_luid_0x0_0x1_phys_0_eng_0_engtype_3D"] = 25, // same engine, other process: 55 total
            ["pid_10_luid_0x0_0x1_phys_0_eng_4_engtype_VideoDecode"] = 40,
            ["pid_30_luid_0x0_0x2_phys_0_eng_0_engtype_3D"] = 5, // second GPU
        };

        MetricsCalculator.GpuUsagePercent(engines).ShouldBe(55);
    }

    [Fact]
    public void GpuUsage_IsClampedToOneHundred()
    {
        var engines = new Dictionary<string, double>
        {
            ["pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D"] = 80,
            ["pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D"] = 40,
        };

        MetricsCalculator.GpuUsagePercent(engines).ShouldBe(100);
    }

    [Fact]
    public void GpuUsage_NoSamples_IsNull()
    {
        MetricsCalculator.GpuUsagePercent(new Dictionary<string, double>()).ShouldBeNull();
    }

    [Theory]
    [InlineData("rapl_package0_pkg", true)]
    [InlineData("RAPL_Package0_PKG", true)] // Intel systems use upper case
    [InlineData("rapl_package1_pkg", true)]
    [InlineData("rapl_package0_core3_core", false)]
    [InlineData("rapl_package0_dram", false)]
    [InlineData("_total", false)]
    public void IsCpuPackageInstance_OnlyWholePackages(string instance, bool expected)
    {
        MetricsCalculator.IsCpuPackageInstance(instance).ShouldBe(expected);
    }

    [Fact]
    public void CpuPackagePower_IsPackageMilliwattsInWatts_IgnoringCoresAndTotal()
    {
        var meters = new Dictionary<string, double>
        {
            ["rapl_package0_core0_core"] = 930,
            ["rapl_package0_pkg"] = 56_050,
            ["_total"] = 0,
        };

        MetricsCalculator.CpuPackagePowerWatts(meters)!.Value.ShouldBe(56.05, tolerance: 0.0001);
    }

    [Fact]
    public void CpuPackagePower_TwoSockets_AreSummed()
    {
        var meters = new Dictionary<string, double> { ["rapl_package0_pkg"] = 40_000, ["rapl_package1_pkg"] = 35_000 };

        MetricsCalculator.CpuPackagePowerWatts(meters).ShouldBe(75);
    }

    [Fact]
    public void CpuPackagePower_NoPackageInstance_IsNull()
    {
        MetricsCalculator.CpuPackagePowerWatts(new Dictionary<string, double>()).ShouldBeNull();
        MetricsCalculator.CpuPackagePowerWatts(new Dictionary<string, double> { ["rapl_package0_core0_core"] = 900 }).ShouldBeNull();
    }

    [Fact]
    public void GpuTemperature_IsTheHottestReportingAdapter()
    {
        GpuAdapterSensors[] adapters = [new(1, 47, 3.2, 1095), new(2, 0, 0, 0), new(3, 61.5, 20, 1500)];

        MetricsCalculator.GpuTemperatureCelsius(adapters).ShouldBe(61.5);
    }

    [Fact]
    public void GpuTemperature_NoneReported_IsNull()
    {
        MetricsCalculator.GpuTemperatureCelsius([new GpuAdapterSensors(1, 0, 0, 0)]).ShouldBeNull();
    }

    [Fact]
    public void GpuPower_IsTheHighestLevel_AndZeroIsAValidReading()
    {
        MetricsCalculator.GpuPowerPercent([new GpuAdapterSensors(1, 47, 3.2, 1095), new GpuAdapterSensors(2, 50, 12.5, 0)]).ShouldBe(12.5);
        MetricsCalculator.GpuPowerPercent([new GpuAdapterSensors(1, 47, 0, 1095)]).ShouldBe(0, "a GPU that reports other sensors at 0% power is idle, not missing");
        MetricsCalculator.GpuPowerPercent([new GpuAdapterSensors(1, 0, 0, 0)]).ShouldBeNull("software adapters report nothing");
    }

    private static NetworkAdapterCounters Adapter(string id, long received, long sent, string description = "Intel(R) Ethernet") =>
        new(id, description, NetworkAdapterKind.Ethernet, true, received, sent);
}
