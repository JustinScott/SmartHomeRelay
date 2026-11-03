namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>Hardware entity keys (spec 5.8). Shipped keys are permanent: never rename them.</summary>
public static class HardwareKeys
{
    /// <summary>sensor (°C): CPU package temperature (AMD Tctl/Tdie, Intel CPU Package).</summary>
    public const string CpuTemperature = "cpu_temperature";

    /// <summary>sensor (RPM): the motherboard fan named for the CPU, or the one set in sensorOverrides.</summary>
    public const string CpuFan = "cpu_fan";

    /// <summary>sensor (°C): temperature of the physical disk holding Windows.</summary>
    public const string SsdTemperature = "ssd_temperature";

    /// <summary>sensor (enum, diagnostic): <see cref="HardwareStatus"/>.</summary>
    public const string Status = "hardware_status";

    /// <summary>sensor (diagnostic): the LibreHardwareMonitor version being read.</summary>
    public const string LhmVersion = "lhm_version";

    /// <summary>The curated sensors, in entity order.</summary>
    public static readonly IReadOnlyList<HardwareSensorDefinition> Sensors =
    [
        new(CpuTemperature, "CPU temperature", "Temperature", "°C", "°C", "temperature", "mdi:thermometer", -40, 150),
        new(CpuFan, "CPU fan", "Fan", "RPM", "RPM", null, "mdi:fan", 0, 20_000),
        new(SsdTemperature, "SSD temperature", "Temperature", "°C", "°C", "temperature", "mdi:harddisk", -40, 150),
    ];

    /// <summary>The keys users may list in <c>features.hardware.sensors</c>.</summary>
    public static readonly IReadOnlyList<string> SensorKeys = [.. Sensors.Select(s => s.Key)];

    /// <summary>True for a curated sensor key.</summary>
    public static bool IsSensorKey(string key) => SensorKeys.Contains(key, StringComparer.Ordinal);

    /// <summary>The definition for a curated key.</summary>
    public static HardwareSensorDefinition Get(string key) => Sensors.Single(s => s.Key == key);
}
