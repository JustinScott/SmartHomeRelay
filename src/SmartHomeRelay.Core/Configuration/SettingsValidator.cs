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
    internal const int MinHardwareIntervalSeconds = 5;
    internal const int MaxHardwareIntervalSeconds = 3_600;

    internal const long MaxMediaDownloadBytes = 200L * 1024 * 1024;
    internal const int MaxMediaDownloadTimeoutSeconds = 300;
    internal const int MaxPositionIntervalSeconds = 300;

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

        ValidateHardware(features.Hardware, errors);
        ValidateMediaPlayer(features.MediaPlayer, errors);
    }

    private static void ValidateMediaPlayer(MediaPlayerSettings media, List<string> errors)
    {
        if (media.Interruption is not (MediaPlayerSettings.InterruptionDuck or MediaPlayerSettings.InterruptionPause))
        {
            errors.Add("features.mediaPlayer.interruption must be \"duck\" or \"pause\".");
        }

        ValidateRange("features.mediaPlayer.duckLevelPercent", media.DuckLevelPercent, 0, 100, errors);
        ValidateRange("features.mediaPlayer.maxDownloadBytes", media.MaxDownloadBytes, 1, MaxMediaDownloadBytes, errors);
        ValidateRange("features.mediaPlayer.downloadTimeoutSeconds", media.DownloadTimeoutSeconds, 1, MaxMediaDownloadTimeoutSeconds, errors);
        ValidateRange("features.mediaPlayer.positionIntervalSeconds", media.PositionIntervalSeconds, 1, MaxPositionIntervalSeconds, errors);
    }

    private static void ValidateHardware(HardwareSettings hardware, List<string> errors)
    {
        ValidateRange("features.hardware.port", hardware.Port, 1, 65_535, errors);
        ValidateRange(
            "features.hardware.intervalSeconds",
            hardware.IntervalSeconds,
            MinHardwareIntervalSeconds,
            MaxHardwareIntervalSeconds,
            errors);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in hardware.Sensors ?? [])
        {
            if (!Features.Hardware.HardwareKeys.IsSensorKey(key))
            {
                errors.Add($"features.hardware.sensors: unknown sensor '{key}'. Known: {string.Join(", ", Features.Hardware.HardwareKeys.SensorKeys)}.");
            }
            else if (!seen.Add(key))
            {
                errors.Add($"features.hardware.sensors lists '{key}' more than once.");
            }
        }

        foreach (var (key, sensorId) in hardware.SensorOverrides)
        {
            if (!Features.Hardware.HardwareKeys.IsSensorKey(key))
            {
                errors.Add($"features.hardware.sensorOverrides: unknown sensor '{key}'.");
            }

            if (string.IsNullOrWhiteSpace(sensorId) || !sensorId.StartsWith('/') || sensorId.Any(char.IsWhiteSpace))
            {
                errors.Add($"features.hardware.sensorOverrides.{key} must be a LibreHardwareMonitor sensor ID such as /lpc/nct6798d/0/fan/1.");
            }
        }
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
