namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Physical memory, in bytes.</summary>
/// <param name="TotalBytes">Installed memory usable by Windows.</param>
/// <param name="AvailableBytes">Memory available right now.</param>
public readonly record struct MemoryStatus(ulong TotalBytes, ulong AvailableBytes);
