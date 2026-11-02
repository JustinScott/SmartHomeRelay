namespace SmartHomeRelay.Core.Mqtt;

/// <summary>The app version (set once in Directory.Build.props, spec 8), reported as HA <c>sw_version</c>.</summary>
public static class RelayVersion
{
    /// <summary>Major.minor.patch, e.g. <c>0.1.0</c>.</summary>
    public static string Current { get; } = typeof(RelayVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
