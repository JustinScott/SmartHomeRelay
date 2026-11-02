using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>
/// Compares JSON against an approved file in <c>Snapshots/</c> (spec 9). On a mismatch it writes
/// <c>{name}.received.json</c> next to it: review the diff and, if the change is intended, replace the
/// approved file with it. Discovery payloads are what HA sees, so changes must be deliberate.
/// </summary>
internal static class Snapshot
{
    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    public static void ShouldMatchApproved(string json, string name, [CallerFilePath] string callerPath = "")
    {
        var directory = Path.Combine(FindTestProjectDirectory(callerPath), "Snapshots");
        var approvedPath = Path.Combine(directory, $"{name}.approved.json");
        var receivedPath = Path.Combine(directory, $"{name}.received.json");

        using var document = JsonDocument.Parse(json);
        var actual = JsonSerializer.Serialize(document.RootElement, s_indented).ReplaceLineEndings("\n") + "\n";

        var approved = File.Exists(approvedPath) ? File.ReadAllText(approvedPath).ReplaceLineEndings("\n") : null;
        if (approved == actual)
        {
            File.Delete(receivedPath);
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(receivedPath, actual);
        throw new Xunit.Sdk.XunitException(approved is null
            ? $"No approved snapshot '{name}'. Review {receivedPath} and rename it to {name}.approved.json."
            : $"Snapshot '{name}' changed. Compare {receivedPath} with {approvedPath}; if intended, replace the approved file.");
    }

    private static string FindTestProjectDirectory(string callerPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(callerPath)!);
        while (directory is not null && !directory.EnumerateFiles("*.csproj").Any())
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Test project directory not found.");
    }
}
