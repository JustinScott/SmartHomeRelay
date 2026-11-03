namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>The anti-spoofing verdict: null <see cref="Problem"/> means the endpoint may be read.</summary>
/// <param name="Problem">The failing status, or null when trusted.</param>
/// <param name="Version">The LibreHardwareMonitor version that will be read, if known.</param>
/// <param name="Reason">A short explanation for the log and the tray; never secrets.</param>
public sealed record LhmTrustResult(HardwareStatus? Problem, Version? Version, string Reason)
{
    /// <summary>True when the endpoint may be read.</summary>
    public bool IsTrusted => Problem is null;
}
