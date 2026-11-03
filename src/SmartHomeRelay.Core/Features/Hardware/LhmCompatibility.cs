namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>
/// The tested LibreHardwareMonitor versions (spec 5.8). To support a new release: save its
/// <c>data.json</c> as a test fixture, run the tests, and move <see cref="TestedVersion"/> in the same commit
/// (and the table in docs/hardware-sensors.md).
/// </summary>
public static class LhmCompatibility
{
    /// <summary>The oldest usable version: earlier ones have no stable sensor IDs.</summary>
    public static readonly Version MinimumVersion = new(0, 9, 5);

    /// <summary>The newest version tested against saved <c>data.json</c> samples.</summary>
    public static readonly Version TestedVersion = new(0, 9, 6);

    /// <summary>Classifies <paramref name="version"/> (compared on major.minor.build).</summary>
    public static LhmVersionSupport Classify(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var normalized = Normalize(version);
        if (normalized < MinimumVersion)
        {
            return LhmVersionSupport.TooOld;
        }

        return normalized > TestedVersion ? LhmVersionSupport.Newer : LhmVersionSupport.Tested;
    }

    /// <summary>Display form, e.g. <c>0.9.6</c>.</summary>
    public static string Format(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return Normalize(version).ToString(3);
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));
}
