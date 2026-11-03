namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>The MQTT state values of <see cref="HardwareStatus"/>.</summary>
public static class HardwareStatusNames
{
    /// <summary>Every value, in HA enum option order.</summary>
    public static readonly IReadOnlyList<string> All =
        [.. Enum.GetValues<HardwareStatus>().Select(ToName)];

    /// <summary>States in which hardware sensors are available.</summary>
    public static readonly IReadOnlyList<string> Usable = [ToName(HardwareStatus.Ok), ToName(HardwareStatus.NewerThanTested)];

    /// <summary>The published value, e.g. <c>newer_than_tested</c>.</summary>
    public static string ToName(HardwareStatus status) => status switch
    {
        HardwareStatus.Ok => "ok",
        HardwareStatus.NewerThanTested => "newer_than_tested",
        HardwareStatus.NotRunning => "not_running",
        HardwareStatus.LoginFailed => "login_failed",
        HardwareStatus.Untrusted => "untrusted",
        HardwareStatus.Incompatible => "incompatible",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown hardware status."),
    };
}
