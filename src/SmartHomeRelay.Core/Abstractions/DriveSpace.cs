namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Size and free space of a fixed drive.</summary>
/// <param name="Letter">Drive letter, e.g. <c>C</c>.</param>
/// <param name="TotalBytes">Drive size.</param>
/// <param name="FreeBytes">Free space available to the user.</param>
public sealed record DriveSpace(char Letter, ulong TotalBytes, ulong FreeBytes);
