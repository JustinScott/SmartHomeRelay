using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>
/// Decides whether what answers on LibreHardwareMonitor's port can be trusted (spec 5.8, anti-spoofing):
/// a running <c>LibreHardwareMonitor.exe</c> in an admin-only folder at a usable version, and a port owned
/// either by HTTP.sys (which needs admin rights to register) or by that program itself.
/// </summary>
public static class LhmTrustEvaluator
{
    /// <summary>LibreHardwareMonitor's program file name.</summary>
    public const string ProgramFileName = "LibreHardwareMonitor.exe";

    /// <summary>Evaluates <paramref name="snapshot"/>.</summary>
    public static LhmTrustResult Evaluate(LhmEndpointSnapshot snapshot, int port)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Processes.Count == 0)
        {
            return new(HardwareStatus.NotRunning, null, "LibreHardwareMonitor is not running");
        }

        if (snapshot.Processes.Any(p => !p.InProtectedFolder))
        {
            return new(HardwareStatus.Untrusted, null, "LibreHardwareMonitor runs from a folder that standard users can change; install it under Program Files");
        }

        Version? version;
        switch (snapshot.Owner)
        {
            case PortOwnerKind.None:
                return new(HardwareStatus.NotRunning, null, $"nothing is listening on port {port}; turn on LibreHardwareMonitor's web server");
            case PortOwnerKind.HttpSys:
                // LHM's web server always runs on HTTP.sys. With several copies running, use the newest version.
                version = snapshot.Processes.Select(p => p.Version).Where(v => v is not null).Max();
                break;
            case PortOwnerKind.Process when IsTrustedProgram(snapshot.OwnerImagePath) && snapshot.OwnerInProtectedFolder:
                version = snapshot.OwnerVersion;
                break;
            case PortOwnerKind.Process:
                return new(HardwareStatus.Untrusted, null, $"port {port} belongs to {FileName(snapshot.OwnerImagePath) ?? "an unknown program"}, not LibreHardwareMonitor");
            default:
                return new(HardwareStatus.Untrusted, null, $"the owner of port {port} could not be determined");
        }

        if (version is null)
        {
            return new(HardwareStatus.Untrusted, null, "LibreHardwareMonitor's version could not be read");
        }

        return LhmCompatibility.Classify(version) == LhmVersionSupport.TooOld
            ? new(HardwareStatus.Incompatible, version, $"LibreHardwareMonitor {LhmCompatibility.Format(version)} is older than {LhmCompatibility.Format(LhmCompatibility.MinimumVersion)}")
            : new(null, version, "trusted");
    }

    private static bool IsTrustedProgram(string? imagePath) =>
        string.Equals(FileName(imagePath), ProgramFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The file name of a Windows path (Core is OS-neutral, so not <see cref="Path.GetFileName(string)"/>).</summary>
    private static string? FileName(string? path) =>
        path is null ? null : path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
}
