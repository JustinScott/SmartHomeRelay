namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>One sensor from LibreHardwareMonitor's <c>data.json</c>.</summary>
/// <param name="HardwareId">The nearest enclosing hardware's ID, e.g. <c>/amdcpu/0</c>.</param>
/// <param name="SensorId">The sensor's ID, e.g. <c>/amdcpu/0/temperature/1</c>.</param>
/// <param name="Name">Display name, e.g. <c>Core (Tctl/Tdie)</c>. Users can rename sensors in LHM.</param>
/// <param name="Type">Sensor type, e.g. <c>Temperature</c>.</param>
/// <param name="Value">The parsed raw value, or null when LHM has none.</param>
/// <param name="Unit">The unit after the number in <c>RawValue</c>, e.g. <c>°C</c>.</param>
public sealed record LhmSensor(string HardwareId, string SensorId, string Name, string Type, double? Value, string Unit);
