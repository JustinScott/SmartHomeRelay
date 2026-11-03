namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>How a LibreHardwareMonitor version compares with what has been tested.</summary>
public enum LhmVersionSupport
{
    /// <summary>Older than <see cref="LhmCompatibility.MinimumVersion"/>: not usable.</summary>
    TooOld,

    /// <summary>Within the tested range.</summary>
    Tested,

    /// <summary>Newer than <see cref="LhmCompatibility.TestedVersion"/>: tried, with a warning.</summary>
    Newer,
}
