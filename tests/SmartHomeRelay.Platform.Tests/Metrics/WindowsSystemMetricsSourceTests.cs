using System.Net.NetworkInformation;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Metrics;

namespace SmartHomeRelay.Platform.Tests.Metrics;

/// <summary>The real Windows counters (read-only, safe to run anywhere Windows runs).</summary>
public sealed class WindowsSystemMetricsSourceTests
{
    private readonly WindowsSystemMetricsSource _source = new();

    [Theory]
    [InlineData(NetworkInterfaceType.Ethernet, NetworkAdapterKind.Ethernet)]
    [InlineData(NetworkInterfaceType.GigabitEthernet, NetworkAdapterKind.Ethernet)]
    [InlineData(NetworkInterfaceType.Wireless80211, NetworkAdapterKind.Wireless)]
    [InlineData(NetworkInterfaceType.Loopback, NetworkAdapterKind.Loopback)]
    [InlineData(NetworkInterfaceType.Tunnel, NetworkAdapterKind.Tunnel)]
    [InlineData(NetworkInterfaceType.Ppp, NetworkAdapterKind.Other)]
    public void Map_TranslatesAdapterTypes(NetworkInterfaceType type, NetworkAdapterKind expected)
    {
        WindowsSystemMetricsSource.Map(type).ShouldBe(expected);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void GetCpuTimes_KernelIncludesIdle()
    {
        var times = _source.GetCpuTimes();

        times.Kernel.ShouldBeGreaterThanOrEqualTo(times.Idle);
        (times.Kernel + times.User).ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void GetMemory_AvailableIsBelowTotal()
    {
        var memory = _source.GetMemory();

        memory.TotalBytes.ShouldBeGreaterThan(0UL);
        memory.AvailableBytes.ShouldBeLessThanOrEqualTo(memory.TotalBytes);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void GetFixedDrives_IncludesTheSystemDrive()
    {
        var systemDrive = char.ToUpperInvariant(Environment.SystemDirectory[0]);

        _source.GetFixedDrives().ShouldContain(d => d.Letter == systemDrive && d.TotalBytes > 0);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void GetNetworkAdapters_ReturnsWithoutThrowing()
    {
        Should.NotThrow(() => _source.GetNetworkAdapters());
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void GetGpuEngineUtilization_TwoReads_ReturnPercentagesOrNothing()
    {
        _source.GetGpuEngineUtilization(); // primes
        var utilization = _source.GetGpuEngineUtilization();

        utilization.Values.ShouldAllBe(v => v >= 0 && v <= 100.5);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void GetGpuAdapterSensors_ReturnsPlausibleValuesOrNothing()
    {
        // CI VMs have no GPU sensors; BOX1 has an AMD card. Either way: no throw, no nonsense.
        var sensors = _source.GetGpuAdapterSensors();

        sensors.ShouldAllBe(s => s.TemperatureCelsius >= 0 && s.TemperatureCelsius < 150);
        sensors.ShouldAllBe(s => s.PowerPercent >= 0 && s.PowerPercent <= 200);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void GetGpuAdapterSensors_RepeatedCalls_DoNotLeakOrFail()
    {
        // Each call opens and closes a handle per adapter.
        var first = _source.GetGpuAdapterSensors().Count;
        for (var i = 0; i < 50; i++)
        {
            _source.GetGpuAdapterSensors();
        }

        _source.GetGpuAdapterSensors().Count.ShouldBe(first);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void EnergyMeter_TwoReads_ReturnNonNegativeMilliwattsOrNothing()
    {
        _ = _source.GetEnergyMeterInstances();
        _source.GetEnergyMeterPowerMilliwatts(); // primes
        var power = _source.GetEnergyMeterPowerMilliwatts();

        power.Values.ShouldAllBe(v => v >= 0);
    }
}
