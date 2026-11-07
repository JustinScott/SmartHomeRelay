using System.Reflection;

namespace SmartHomeRelay.Core.Mqtt;

/// <summary>
/// The app version (spec 8): the number is set once in Directory.Build.props, and the build adds which code it is
/// (Directory.Build.targets). Reported as HA <c>sw_version</c> and the Version sensor, and shown on the PC.
/// </summary>
public static class RelayVersion
{
    /// <summary>Major.minor.patch, e.g. <c>1.0.0</c>. Declared first: <see cref="Current"/> falls back to it.</summary>
    public static string Number { get; } = typeof(RelayVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// The full version: <c>1.0.0</c> for a release, <c>1.0.0-dev+53b3b09</c> for any other build
    /// (<c>.modified</c> appended when built with uncommitted changes).
    /// </summary>
    public static string Current { get; } =
        typeof(RelayVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Number;

    /// <summary>
    /// The version for people: <see cref="Current"/>, plus "(development build)" when this copy isn't the installed
    /// one, e.g. run from a source folder.
    /// </summary>
    public static string Describe(bool isInstalled) => Describe(Current, isInstalled);

    /// <summary><see cref="Describe(bool)"/> for a given version. For tests.</summary>
    public static string Describe(string version, bool isInstalled) =>
        isInstalled ? version : $"{version} (development build)";
}
