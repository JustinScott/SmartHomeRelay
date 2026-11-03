namespace SmartHomeRelay.Core.Abstractions;

/// <summary>A running <c>LibreHardwareMonitor.exe</c>.</summary>
/// <param name="ImagePath">Full path of the program file, or null if Windows wouldn't say.</param>
/// <param name="Version">The program file's version, or null if unreadable.</param>
/// <param name="InProtectedFolder">True if only administrators can write to the program's folder.</param>
public sealed record LhmProcessInfo(string? ImagePath, Version? Version, bool InProtectedFolder);
