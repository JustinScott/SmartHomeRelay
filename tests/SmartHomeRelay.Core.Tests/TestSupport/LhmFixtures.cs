using System.Text.Json.Nodes;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>Saved LibreHardwareMonitor data.json samples (Fixtures/Lhm).</summary>
internal static class LhmFixtures
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Lhm");

    /// <summary>BOX1 (Ryzen 7 5800X3D, NCT6798D, NVMe system disk 0) on LHM 0.9.6.</summary>
    public static string Box1 => File.ReadAllText(Path.Combine(Directory, "box1-0.9.6-synthetic.json"));

    public static IEnumerable<string> All => System.IO.Directory.GetFiles(Directory, "*.json");

    /// <summary>Returns <paramref name="json"/> with one sensor's RawValue replaced.</summary>
    public static string WithRawValue(string json, string sensorId, string rawValue)
    {
        var root = JsonNode.Parse(json)!;
        Find(root, sensorId)!["RawValue"] = rawValue;
        return root.ToJsonString();
    }

    /// <summary>Returns <paramref name="json"/> with one property removed from one sensor.</summary>
    public static string Without(string json, string sensorId, string property)
    {
        var root = JsonNode.Parse(json)!;
        Find(root, sensorId)!.AsObject().Remove(property);
        return root.ToJsonString();
    }

    private static JsonNode? Find(JsonNode node, string sensorId)
    {
        if (node["SensorId"]?.GetValue<string>() == sensorId)
        {
            return node;
        }

        foreach (var child in node["Children"]!.AsArray())
        {
            if (Find(child!, sensorId) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
