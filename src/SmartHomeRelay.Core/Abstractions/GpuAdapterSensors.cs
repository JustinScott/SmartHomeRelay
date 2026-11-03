namespace SmartHomeRelay.Core.Abstractions;

/// <summary>
/// Sensor readings Windows reports for one GPU adapter (the data behind Task Manager's GPU temperature).
/// A value of 0 means the driver doesn't report it.
/// </summary>
/// <param name="AdapterId">Identifies the adapter (its LUID). The same physical GPU can appear under several IDs.</param>
/// <param name="TemperatureCelsius">GPU temperature in °C.</param>
/// <param name="PowerPercent">Power draw as a percentage of the card's maximum (not watts).</param>
/// <param name="FanRpm">Fan speed.</param>
public sealed record GpuAdapterSensors(long AdapterId, double TemperatureCelsius, double PowerPercent, uint FanRpm);
