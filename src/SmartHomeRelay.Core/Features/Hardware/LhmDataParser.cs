using System.Globalization;
using System.Text.Json;

namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>
/// Parses LibreHardwareMonitor's <c>data.json</c> (spec 5.8). It is the data behind LHM's web page, not a
/// documented API, so everything is checked: a tree of nodes with <c>Text</c> and <c>Children</c>, and
/// sensors with <c>SensorId</c>, <c>Type</c> and <c>RawValue</c>. Anything else is reported as a format
/// problem rather than guessed at.
/// </summary>
public static class LhmDataParser
{
    /// <summary>Deepest nesting accepted (LHM's tree is about 6 levels).</summary>
    public const int MaxDepth = 32;

    /// <summary>Most nodes accepted.</summary>
    public const int MaxNodes = 50_000;

    private static readonly JsonDocumentOptions s_options = new() { MaxDepth = MaxDepth + 8 };

    /// <summary>Parses <paramref name="json"/>.</summary>
    public static LhmParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, s_options);
        }
        catch (JsonException ex)
        {
            return LhmParseResult.Invalid($"data.json is not valid JSON ({ex.Message})");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Children", out var children) || children.ValueKind != JsonValueKind.Array)
            {
                return LhmParseResult.Invalid("data.json has no top-level Children list");
            }

            var sensors = new List<LhmSensor>();
            var nodes = 0;
            var problem = Walk(root, hardwareId: string.Empty, depth: 0, sensors, ref nodes);
            if (problem is not null)
            {
                return LhmParseResult.Invalid(problem);
            }

            return sensors.Count == 0
                ? LhmParseResult.Invalid("data.json contains no sensors")
                : new LhmParseResult(sensors, null);
        }
    }

    /// <summary>
    /// Parses a <c>RawValue</c> such as <c>"45.3 °C"</c>, <c>"45,3 °C"</c> (LHM formats with the PC's
    /// culture), <c>"1095 RPM"</c>, or <c>" °C"</c> (no value). Returns false if the number is unreadable.
    /// </summary>
    public static bool TryParseRawValue(string raw, out double? value, out string unit)
    {
        ArgumentNullException.ThrowIfNull(raw);

        value = null;
        var text = raw.TrimEnd();
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        var number = (space < 0 ? text : text[..space]).Trim();
        unit = space < 0 ? string.Empty : text[(space + 1)..].Trim();

        if (number.Length == 0)
        {
            return true; // LHM has no reading for this sensor right now.
        }

        // No culture's fixed-point format has digit grouping, so a comma can only be the decimal separator.
        // Some cultures (e.g. sv-SE) use U+2212 as the minus sign.
        var normalized = number.Replace(',', '.').Replace('−', '-');
        if (!double.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed))
        {
            unit = text.Trim();
            return false;
        }

        value = parsed;
        return true;
    }

    private static string? Walk(JsonElement node, string hardwareId, int depth, List<LhmSensor> sensors, ref int nodes)
    {
        if (depth > MaxDepth)
        {
            return "data.json is nested deeper than expected";
        }

        if (++nodes > MaxNodes)
        {
            return "data.json has more nodes than expected";
        }

        if (node.ValueKind != JsonValueKind.Object
            || !node.TryGetProperty("Text", out var text) || text.ValueKind != JsonValueKind.String
            || !node.TryGetProperty("Children", out var children) || children.ValueKind != JsonValueKind.Array)
        {
            return "a data.json node has no Text or Children";
        }

        if (node.TryGetProperty("HardwareId", out var hardware) && hardware.ValueKind == JsonValueKind.String)
        {
            hardwareId = hardware.GetString()!;
        }

        if (node.TryGetProperty("SensorId", out var sensorIdElement))
        {
            var sensor = ReadSensor(node, sensorIdElement, text.GetString()!, hardwareId, out var problem);
            if (sensor is null)
            {
                return problem;
            }

            sensors.Add(sensor);
        }

        foreach (var child in children.EnumerateArray())
        {
            var problem = Walk(child, hardwareId, depth + 1, sensors, ref nodes);
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }

    private static LhmSensor? ReadSensor(JsonElement node, JsonElement sensorIdElement, string name, string hardwareId, out string? problem)
    {
        problem = null;
        if (sensorIdElement.ValueKind != JsonValueKind.String || sensorIdElement.GetString() is not { Length: > 1 } sensorId || sensorId[0] != '/')
        {
            problem = "a sensor's SensorId is not an ID like /amdcpu/0/temperature/1";
            return null;
        }

        if (!node.TryGetProperty("Type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            problem = $"sensor {sensorId} has no Type";
            return null;
        }

        if (!node.TryGetProperty("RawValue", out var rawValue) || rawValue.ValueKind != JsonValueKind.String)
        {
            problem = $"sensor {sensorId} has no RawValue";
            return null;
        }

        // An unreadable number is kept as "no value, unit = the raw text": only a mapped sensor makes it a problem.
        TryParseRawValue(rawValue.GetString()!, out var value, out var unit);
        return new LhmSensor(hardwareId, sensorId, name, type.GetString()!, value, unit);
    }
}
