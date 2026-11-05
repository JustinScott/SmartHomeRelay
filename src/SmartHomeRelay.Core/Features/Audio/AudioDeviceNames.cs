using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Features.Audio;

/// <summary>
/// Turns the device list into the <c>output_device</c> select's options: Windows' names, made unique (two
/// identical headsets become "Headset" and "Headset (2)"), mapped back to device IDs.
/// </summary>
public static class AudioDeviceNames
{
    /// <summary>Shown when there is no playback device, because HA needs at least one option.</summary>
    public const string NoDevice = "No output device";

    /// <summary>Unique option names in device order, each with its device ID.</summary>
    public static IReadOnlyList<(string Option, string Id)> Build(IReadOnlyList<AudioDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(string, string)>();
        foreach (var device in devices)
        {
            var baseName = string.IsNullOrWhiteSpace(device.Name) ? "Unnamed device" : device.Name.Trim();
            var name = baseName;
            for (var n = 2; !used.Add(name); n++)
            {
                name = $"{baseName} ({n})";
            }

            result.Add((name, device.Id));
        }

        return result;
    }
}
