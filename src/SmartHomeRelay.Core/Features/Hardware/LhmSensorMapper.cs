using System.Globalization;

namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>
/// Picks the LibreHardwareMonitor sensor behind each curated key (spec 5.8). Rules use LHM's sensor IDs
/// and types, which are stable, and names only as a tiebreak (users can rename sensors in LHM). A
/// <c>sensorOverrides</c> entry wins over the rule.
/// </summary>
public static class LhmSensorMapper
{
    /// <summary>CPU temperature sensor names, best first: AMD Zen, Intel, older AMD.</summary>
    private static readonly string[] s_cpuTemperatureNames = ["Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Package"];

    /// <summary>Storage temperature names, best first (NVMe reports a composite).</summary>
    private static readonly string[] s_diskTemperatureNames = ["Composite Temperature", "Temperature"];

    private static readonly string[] s_storageKinds = ["nvme", "ssd", "hdd"];

    /// <summary>Finds the sensor for <paramref name="key"/>, or null if none matches.</summary>
    public static LhmSensor? Find(
        string key,
        IReadOnlyList<LhmSensor> sensors,
        IReadOnlyDictionary<string, string> overrides,
        int? systemDiskNumber)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(overrides);

        var definition = HardwareKeys.Get(key);
        var ofType = sensors.Where(s => string.Equals(s.Type, definition.LhmType, StringComparison.Ordinal)).ToList();

        if (overrides.TryGetValue(key, out var sensorId))
        {
            return ofType.FirstOrDefault(s => string.Equals(s.SensorId, sensorId, StringComparison.OrdinalIgnoreCase));
        }

        return key switch
        {
            HardwareKeys.CpuTemperature => ByName(ofType.Where(s => IsCpu(s.HardwareId)), s_cpuTemperatureNames),
            HardwareKeys.CpuFan => ofType
                .Where(s => s.HardwareId.StartsWith("/lpc/", StringComparison.OrdinalIgnoreCase)
                    && s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.SensorId, StringComparer.Ordinal)
                .FirstOrDefault(),
            HardwareKeys.SsdTemperature => systemDiskNumber is { } disk
                ? ByName(ofType.Where(s => IsDisk(s.HardwareId, disk)), s_diskTemperatureNames)
                    ?? ofType.Where(s => IsDisk(s.HardwareId, disk)).OrderBy(s => s.SensorId, StringComparer.Ordinal).FirstOrDefault()
                : null,
            _ => null,
        };
    }

    private static bool IsCpu(string hardwareId) =>
        hardwareId.StartsWith("/amdcpu/", StringComparison.OrdinalIgnoreCase)
        || hardwareId.StartsWith("/intelcpu/", StringComparison.OrdinalIgnoreCase);

    /// <summary>LHM storage IDs end in the physical disk number: <c>/nvme/0</c>, <c>/ssd/1</c>, <c>/hdd/2</c>.</summary>
    private static bool IsDisk(string hardwareId, int diskNumber)
    {
        var number = diskNumber.ToString(CultureInfo.InvariantCulture);
        return s_storageKinds.Any(kind => string.Equals(hardwareId, $"/{kind}/{number}", StringComparison.OrdinalIgnoreCase));
    }

    private static LhmSensor? ByName(IEnumerable<LhmSensor> candidates, string[] names)
    {
        var list = candidates.ToList();
        foreach (var name in names)
        {
            // Prefer one with a value: LHM lists inactive sensors (e.g. Tctl on Zen 3) without one.
            var matches = list.Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 0)
            {
                return matches.FirstOrDefault(s => s.Value is not null) ?? matches[0];
            }
        }

        return null;
    }
}
