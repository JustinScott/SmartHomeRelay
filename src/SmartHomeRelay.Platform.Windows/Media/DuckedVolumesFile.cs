using System.Text.Json;

namespace SmartHomeRelay.Platform.Windows.Media;

/// <summary>
/// The levels saved before a duck, so they can be restored after a crash or sleep (spec 5.9). Written before
/// any volume changes, deleted once restored.
/// </summary>
internal sealed class DuckedVolumesFile(string path)
{
    public string Path { get; } = path;

    public void Save(IReadOnlyList<DuckedSession> sessions)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(sessions));
        File.Move(temp, Path, overwrite: true);
    }

    /// <summary>The saved sessions, or empty when there's no file or it can't be read.</summary>
    public IReadOnlyList<DuckedSession> Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<List<DuckedSession>>(File.ReadAllText(Path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Delete()
    {
        try
        {
            File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Harmless: the next recovery finds nothing untouched to restore.
        }
    }
}
