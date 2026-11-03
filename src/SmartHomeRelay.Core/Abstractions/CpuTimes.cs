namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Cumulative system CPU times since boot (GetSystemTimes). Kernel time includes idle time.</summary>
/// <param name="Idle">Total idle time across all processors.</param>
/// <param name="Kernel">Total kernel time across all processors, including idle.</param>
/// <param name="User">Total user time across all processors.</param>
public readonly record struct CpuTimes(TimeSpan Idle, TimeSpan Kernel, TimeSpan User);
