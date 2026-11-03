namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>A curated hardware sensor: its entity, the LHM sensor type and unit it maps to, and plausible limits.</summary>
/// <param name="Key">Entity key (permanent once shipped).</param>
/// <param name="Name">Display name in HA.</param>
/// <param name="LhmType">LibreHardwareMonitor's <c>Type</c>, e.g. <c>Temperature</c>.</param>
/// <param name="LhmUnit">The unit LHM appends to <c>RawValue</c> for that type, e.g. <c>°C</c>.</param>
/// <param name="Unit">Unit in HA.</param>
/// <param name="DeviceClass">HA device class, if any.</param>
/// <param name="Icon">Material Design icon.</param>
/// <param name="Min">Lowest plausible value; anything below is dropped.</param>
/// <param name="Max">Highest plausible value; anything above is dropped.</param>
public sealed record HardwareSensorDefinition(
    string Key,
    string Name,
    string LhmType,
    string LhmUnit,
    string Unit,
    string? DeviceClass,
    string Icon,
    double Min,
    double Max);
