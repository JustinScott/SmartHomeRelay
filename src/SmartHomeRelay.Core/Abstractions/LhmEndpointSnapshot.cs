namespace SmartHomeRelay.Core.Abstractions;

/// <summary>What is behind LibreHardwareMonitor's port right now (spec 5.8, anti-spoofing).</summary>
/// <param name="Owner">Who listens on the port.</param>
/// <param name="OwnerImagePath">The listening program file when <see cref="Owner"/> is <see cref="PortOwnerKind.Process"/>.</param>
/// <param name="OwnerInProtectedFolder">True if only administrators can write to that program's folder.</param>
/// <param name="OwnerVersion">That program file's version, if readable.</param>
/// <param name="Processes">Every running <c>LibreHardwareMonitor.exe</c>.</param>
public sealed record LhmEndpointSnapshot(
    PortOwnerKind Owner,
    string? OwnerImagePath,
    bool OwnerInProtectedFolder,
    Version? OwnerVersion,
    IReadOnlyList<LhmProcessInfo> Processes);
