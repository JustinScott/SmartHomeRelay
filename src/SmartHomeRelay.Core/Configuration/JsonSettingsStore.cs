using System.Text.Json;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>Stores settings as JSON at <see cref="IAppPaths.SettingsFilePath"/>.</summary>
public sealed class JsonSettingsStore(IAppPaths paths) : ISettingsStore
{
    /// <inheritdoc />
    public bool Exists() => File.Exists(paths.SettingsFilePath);

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var path = paths.SettingsFilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temp file and swap it in, so a crash mid-write never leaves a truncated settings file.
        var tempPath = path + ".tmp";
        var stream = File.Create(tempPath);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SettingsJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    /// <inheritdoc />
    public string? GetSyntaxError()
    {
        var path = paths.SettingsFilePath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            // Called from options change callbacks, which are synchronous. The file is small.
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text, SettingsJson.DocumentOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? null
                : "settings.json must contain a JSON object.";
        }
        catch (JsonException ex)
        {
            var line = ex.LineNumber is { } n ? n + 1 : 0;
            return $"settings.json is not valid JSON (line {line}).";
        }
        catch (IOException ex)
        {
            return $"settings.json could not be read: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"settings.json could not be read: {ex.Message}";
        }
    }
}
