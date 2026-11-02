using Microsoft.Extensions.Logging;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// Validates <see cref="AppSettings"/> on load and on save (spec 7).
/// Messages name the JSON path so they make sense both in the log and in the settings window.
/// </summary>
public sealed class SettingsValidator
{
    internal const int MinIdleThresholdSeconds = 10;
    internal const int MaxIdleThresholdSeconds = 86_400;
    internal const int MinMetricsIntervalSeconds = 5;
    internal const int MaxMetricsIntervalSeconds = 3_600;
    internal const int MinImageTimeoutSeconds = 1;
    internal const int MaxImageTimeoutSeconds = 120;
    internal const long MaxImageMaxBytes = 100L * 1024 * 1024;
    internal const int MaxShutdownDelaySeconds = 600;

    private static readonly char[] s_topicWildcards = ['+', '#'];

    /// <summary>Returns every problem found; an empty list means the settings are valid.</summary>
    public IReadOnlyList<string> Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<string>();
        ValidateMqtt(settings.Mqtt, errors);
        ValidateDevice(settings.Device, errors);
        ValidateFeatures(settings.Features, errors);

        if (!Enum.IsDefined(settings.LogLevel) || settings.LogLevel == LogLevel.None)
        {
            errors.Add("logLevel must be one of Trace, Debug, Information, Warning, Error, Critical.");
        }

        return errors;
    }

    private static void ValidateMqtt(MqttSettings mqtt, List<string> errors)
    {
        // An empty host is allowed: it means "not configured yet" and the app runs without connecting.
        if (mqtt.Host.Any(char.IsWhiteSpace))
        {
            errors.Add("mqtt.host must not contain spaces.");
        }

        if (mqtt.Port is < 1 or > 65_535)
        {
            errors.Add("mqtt.port must be between 1 and 65535.");
        }

        if (mqtt.UseTls)
        {
            errors.Add("mqtt.useTls is not supported in this version. Set it to false.");
        }

        ValidateTopic("mqtt.discoveryPrefix", mqtt.DiscoveryPrefix, errors);
        ValidateTopic("mqtt.baseTopic", mqtt.BaseTopic, errors);
    }

    private static void ValidateDevice(DeviceSettings device, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(device.Id))
        {
            errors.Add("device.id is required.");
        }
        else if (!device.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            // The ID is a topic level and the node_id in HA discovery topics, which HA restricts to
            // [a-zA-Z0-9_-]. Windows host names already fit.
            errors.Add("device.id may only contain letters, digits, '-', and '_'.");
        }

        if (string.IsNullOrWhiteSpace(device.Name))
        {
            errors.Add("device.name is required.");
        }
    }

    private static void ValidateFeatures(FeaturesSettings features, List<string> errors)
    {
        ValidateRange(
            "features.presence.idleThresholdSeconds",
            features.Presence.IdleThresholdSeconds,
            MinIdleThresholdSeconds,
            MaxIdleThresholdSeconds,
            errors);

        ValidateRange(
            "features.power.shutdownDelaySeconds",
            features.Power.ShutdownDelaySeconds,
            0,
            MaxShutdownDelaySeconds,
            errors);

        ValidateRange(
            "features.metrics.intervalSeconds",
            features.Metrics.IntervalSeconds,
            MinMetricsIntervalSeconds,
            MaxMetricsIntervalSeconds,
            errors);

        ValidateRange(
            "features.notifications.imageDownloadTimeoutSeconds",
            features.Notifications.ImageDownloadTimeoutSeconds,
            MinImageTimeoutSeconds,
            MaxImageTimeoutSeconds,
            errors);

        ValidateRange(
            "features.notifications.imageMaxBytes",
            features.Notifications.ImageMaxBytes,
            1,
            MaxImageMaxBytes,
            errors);
    }

    private static void ValidateTopic(string name, string value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} is required.");
            return;
        }

        if (value.IndexOfAny(s_topicWildcards) >= 0)
        {
            errors.Add($"{name} must not contain the MQTT wildcards '+' or '#'.");
        }

        if (value.StartsWith('/') || value.EndsWith('/'))
        {
            errors.Add($"{name} must not start or end with '/'.");
        }
    }

    private static void ValidateRange(string name, long value, long min, long max, List<string> errors)
    {
        if (value < min || value > max)
        {
            errors.Add($"{name} must be between {min} and {max}.");
        }
    }
}
