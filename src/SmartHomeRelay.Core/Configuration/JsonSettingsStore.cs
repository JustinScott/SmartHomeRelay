using System.Text.Json;
using System.Text.Json.Nodes;
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

        var document = WithPasswordPlaceholder(JsonSerializer.SerializeToNode(settings, SettingsJson.Options)!.AsObject());

        // Write to a temp file and swap it in, so a crash mid-write never leaves a truncated settings file.
        var tempPath = path + ".tmp";
        var stream = File.Create(tempPath);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, document, SettingsJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>
    /// Adds an always-empty <c>"password": ""</c> right after <c>username</c> (MQTT and hardware). Without it, users editing the
    /// file by hand typed their password into <c>passwordProtected</c>, which only holds encrypted values.
    /// The plain password itself is never serialized.
    /// </summary>
    private static JsonObject WithPasswordPlaceholder(JsonObject root)
    {
        if (root["mqtt"] is JsonObject mqtt)
        {
            root["mqtt"] = WithPlaceholder(mqtt);
        }

        // The LibreHardwareMonitor web server password (spec 5.8) works the same way.
        if (root["features"] is JsonObject features && features["hardware"] is JsonObject hardware)
        {
            // Spell out the default sensor list so users can see what to edit.
            if (hardware["sensors"] is null)
            {
                hardware["sensors"] = new JsonArray([.. HardwareSettings.DefaultSensors.Select(s => JsonValue.Create(s))]);
            }

            features["hardware"] = WithPlaceholder(hardware);
        }

        return root;
    }

    private static JsonObject WithPlaceholder(JsonObject section)
    {
        var ordered = new JsonObject();
        foreach (var (key, value) in section.ToList())
        {
            section.Remove(key);
            ordered[key] = value;
            if (key == "username")
            {
                ordered["password"] = string.Empty;
            }
        }

        return ordered;
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
