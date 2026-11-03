namespace SmartHomeRelay.Core.Features.Metrics;

/// <summary>How a metric is formatted and what counts as a meaningful change (spec 5.5 deadband).</summary>
public enum MetricKind
{
    /// <summary>0-100, published as a whole number; changes of 2 points or more are published.</summary>
    Percent,

    /// <summary>Gibibytes (1024^3 bytes) with one decimal; changes of 0.1 GiB or more are published.</summary>
    Gigabytes,

    /// <summary>Mbit/s with one decimal; changes of 10% (and at least 0.1 Mbit/s) are published.</summary>
    Rate,
}
