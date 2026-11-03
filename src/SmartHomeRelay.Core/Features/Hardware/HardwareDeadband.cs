using System.Globalization;

namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>Meaningful-change rules for hardware sensors (spec 5.8), in line with the Metrics deadband.</summary>
public static class HardwareDeadband
{
    /// <summary>Smallest temperature change, in degrees, that is published.</summary>
    public const double Degrees = 2;

    /// <summary>Smallest relative fan speed change that is published.</summary>
    public const double RpmFraction = 0.05;

    /// <summary>Smallest absolute fan speed change, in RPM, that is published.</summary>
    public const double RpmMinimum = 50;

    /// <summary>True if <paramref name="current"/> differs enough from the last published value.</summary>
    public static bool ShouldPublish(HardwareSensorDefinition definition, double? lastPublished, double current)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (lastPublished is not { } last)
        {
            return true;
        }

        var change = Math.Abs(current - last);
        return definition.Unit == "RPM"
            ? change >= Math.Max(RpmMinimum, last * RpmFraction) - 1e-9
            : change >= Degrees - 1e-9;
    }

    /// <summary>The MQTT payload: a whole number.</summary>
    public static string Format(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
}
