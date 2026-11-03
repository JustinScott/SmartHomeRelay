using System.Globalization;

namespace SmartHomeRelay.Core.Features.Metrics;

/// <summary>
/// Formatting and the "meaningful change" rule for metrics (spec 5.5). Each publish becomes a history row
/// in HA, so small wobbles are not published: a busy PC sends a few updates a minute, an idle one almost none.
/// </summary>
public static class MetricDeadband
{
    /// <summary>Smallest change, in percentage points, that is published.</summary>
    public const double PercentPoints = 2;

    /// <summary>Smallest change, in GB, that is published.</summary>
    public const double Gigabytes = 0.1;

    /// <summary>Smallest relative rate change that is published.</summary>
    public const double RateFraction = 0.10;

    /// <summary>Smallest absolute rate change, in Mbit/s, that is published.</summary>
    public const double RateMinimum = 0.1;

    /// <summary>True if <paramref name="current"/> differs enough from the last published value.</summary>
    public static bool ShouldPublish(MetricKind kind, double? lastPublished, double current)
    {
        if (lastPublished is not { } last)
        {
            return true;
        }

        var change = Math.Abs(current - last);
        return kind switch
        {
            MetricKind.Percent => change >= PercentPoints,
            MetricKind.Gigabytes => change >= Gigabytes - 1e-9,
            MetricKind.Rate => change >= Math.Max(RateMinimum, last * RateFraction) - 1e-9,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown metric kind."),
        };
    }

    /// <summary>The MQTT payload: whole percent, or one decimal for GB and rates.</summary>
    public static string Format(MetricKind kind, double value) => kind switch
    {
        MetricKind.Percent => Math.Round(Math.Clamp(value, 0, 100), MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture),
        MetricKind.Gigabytes or MetricKind.Rate => Math.Round(Math.Max(0, value), 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown metric kind."),
    };
}
