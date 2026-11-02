using System.Text;

namespace SmartHomeRelay.Core.Mqtt;

/// <summary>
/// Builds HA <c>unique_id</c> values (spec 4.1). <b>The rule is permanent</b>: changing it orphans every
/// entity in Home Assistant.
/// </summary>
public static class UniqueIds
{
    /// <summary>Lowercases and replaces every character other than <c>a-z</c>, <c>0-9</c>, <c>_</c> with <c>_</c>.</summary>
    public static string SanitizeDeviceId(string deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        var builder = new StringBuilder(deviceId.Length);
        foreach (var c in deviceId.ToLowerInvariant())
        {
            builder.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' ? c : '_');
        }

        return builder.ToString();
    }

    /// <summary><c>{sanitizedDeviceId}_{entityKey}</c>, e.g. <c>desktop_abc123_cpu_usage</c>.</summary>
    public static string For(string deviceId, string entityKey) => $"{SanitizeDeviceId(deviceId)}_{entityKey}";
}
