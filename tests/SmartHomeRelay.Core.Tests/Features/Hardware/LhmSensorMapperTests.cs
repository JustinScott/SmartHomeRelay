using SmartHomeRelay.Core.Features.Hardware;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Hardware;

public sealed class LhmSensorMapperTests
{
    private static readonly Dictionary<string, string> NoOverrides = [];

    private static IReadOnlyList<LhmSensor> Box1 => LhmDataParser.Parse(LhmFixtures.Synthetic).Sensors;

    [Fact]
    public void CpuTemperature_Amd_IsTctlTdie()
    {
        LhmSensorMapper.Find(HardwareKeys.CpuTemperature, Box1, NoOverrides, 0)!.SensorId.ShouldBe("/amdcpu/0/temperature/2");
    }

    [Fact]
    public void CpuTemperature_Intel_IsCpuPackage()
    {
        LhmSensor[] sensors =
        [
            new("/intelcpu/0", "/intelcpu/0/temperature/0", "CPU Core #1", "Temperature", 50, "°C"),
            new("/intelcpu/0", "/intelcpu/0/temperature/8", "CPU Package", "Temperature", 55, "°C"),
        ];

        LhmSensorMapper.Find(HardwareKeys.CpuTemperature, sensors, NoOverrides, 0)!.SensorId.ShouldBe("/intelcpu/0/temperature/8");
    }

    [Fact]
    public void CpuTemperature_PrefersTheCopyWithAReading()
    {
        LhmSensor[] sensors =
        [
            new("/amdcpu/0", "/amdcpu/0/temperature/1", "Core (Tctl/Tdie)", "Temperature", null, "°C"),
            new("/amdcpu/0", "/amdcpu/0/temperature/2", "Core (Tctl/Tdie)", "Temperature", 48, "°C"),
        ];

        LhmSensorMapper.Find(HardwareKeys.CpuTemperature, sensors, NoOverrides, 0)!.SensorId.ShouldBe("/amdcpu/0/temperature/2");
    }

    [Fact]
    public void CpuTemperature_NotFromOtherHardware()
    {
        // The motherboard's "CPU" temperature is a socket sensor, not the CPU's own.
        var withoutCpu = Box1.Where(s => !s.HardwareId.StartsWith("/amdcpu", StringComparison.Ordinal)).ToList();

        LhmSensorMapper.Find(HardwareKeys.CpuTemperature, withoutCpu, NoOverrides, 0).ShouldBeNull();
    }

    [Fact]
    public void CpuFan_IsTheMotherboardFanNamedCpu()
    {
        LhmSensorMapper.Find(HardwareKeys.CpuFan, Box1, NoOverrides, 0)!.SensorId.ShouldBe("/lpc/nct6798d/0/fan/1");
    }

    [Fact]
    public void CpuFan_NoneNamedCpu_IsNotGuessed()
    {
        var unnamed = Box1.Select(s => s with { Name = s.Name.Replace("CPU Fan", "Fan #2", StringComparison.Ordinal) }).ToList();

        LhmSensorMapper.Find(HardwareKeys.CpuFan, unnamed, NoOverrides, 0).ShouldBeNull();
    }

    [Fact]
    public void Override_WinsOverTheRule()
    {
        var overrides = new Dictionary<string, string> { [HardwareKeys.CpuFan] = "/lpc/nct6798d/0/fan/2" };

        LhmSensorMapper.Find(HardwareKeys.CpuFan, Box1, overrides, 0)!.Name.ShouldBe("Chassis Fan #2");
    }

    [Fact]
    public void Override_OfTheWrongType_IsIgnored()
    {
        // A fan key can't be pointed at a temperature.
        var overrides = new Dictionary<string, string> { [HardwareKeys.CpuFan] = "/amdcpu/0/temperature/2" };

        LhmSensorMapper.Find(HardwareKeys.CpuFan, Box1, overrides, 0).ShouldBeNull();
    }

    [Theory]
    [InlineData(0, "/nvme/0/temperature/0")] // NVMe: the composite
    [InlineData(1, "/hdd/1/temperature/0")]
    public void SsdTemperature_IsTheSystemDisk(int disk, string expected)
    {
        LhmSensorMapper.Find(HardwareKeys.SsdTemperature, Box1, NoOverrides, disk)!.SensorId.ShouldBe(expected);
    }

    [Fact]
    public void SsdTemperature_UnknownSystemDisk_IsNotGuessed()
    {
        LhmSensorMapper.Find(HardwareKeys.SsdTemperature, Box1, NoOverrides, null).ShouldBeNull();
        LhmSensorMapper.Find(HardwareKeys.SsdTemperature, Box1, NoOverrides, 7).ShouldBeNull();
    }
}
