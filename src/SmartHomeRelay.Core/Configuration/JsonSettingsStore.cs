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

        await WriteAsync(path, ToDocument(settings), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SettingsCompletion> AddMissingAsync(AppSettings current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);

        var path = paths.SettingsFilePath;
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (JsonNode.Parse(text, documentOptions: SettingsJson.DocumentOptions) is not JsonObject file)
        {
            return new([], false, false);
        }

        var added = new List<string>();
        var completed = Complete(file, ToDocument(current), prefix: string.Empty, added);
        if (added.Count == 0)
        {
            return new([], false, false);
        }

        if (HasComments(text))
        {
            return new(added, false, true);
        }

        await WriteAsync(path, completed, cancellationToken).ConfigureAwait(false);
        return new(added, true, false);
    }

    /// <summary>
    /// <paramref name="file"/> with every key of <paramref name="defaults"/> it lacks, recursively. Keys match
    /// case-insensitively (as configuration binding does). The file's keys keep their order and values; a
    /// missing key goes right after the key that precedes it in <paramref name="defaults"/>.
    /// </summary>
    internal static JsonObject Complete(JsonObject file, JsonObject defaults, string prefix, List<string> added)
    {
        var defaultKeys = defaults.Select(p => p.Key).ToList();
        string? FileKeyFor(string defaultKey) =>
            file.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, defaultKey, StringComparison.OrdinalIgnoreCase));

        // Each missing key goes after the nearest earlier default key the file has (or first, if none).
        var insertAfter = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var atStart = new List<string>();
        string? anchor = null;
        foreach (var key in defaultKeys)
        {
            if (FileKeyFor(key) is { } fileKey)
            {
                anchor = fileKey;
            }
            else if (anchor is null)
            {
                atStart.Add(key);
            }
            else
            {
                (insertAfter.TryGetValue(anchor, out var list) ? list : insertAfter[anchor] = []).Add(key);
            }
        }

        var result = new JsonObject();
        void Add(string key)
        {
            result[key] = defaults[key]?.DeepClone();
            added.Add(prefix + key);
        }

        atStart.ForEach(Add);
        foreach (var (key, value) in file.ToList())
        {
            file.Remove(key);
            var defaultKey = defaultKeys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            result[key] = value is JsonObject child && defaultKey is not null && defaults[defaultKey] is JsonObject childDefaults
                ? Complete(child, childDefaults, $"{prefix}{key}.", added)
                : value;

            if (insertAfter.TryGetValue(key, out var missing))
            {
                missing.ForEach(Add);
            }
        }

        return result;
    }

    private static JsonObject ToDocument(AppSettings settings) =>
        WithPasswordPlaceholder(JsonSerializer.SerializeToNode(settings, SettingsJson.Options)!.AsObject());

    private static bool HasComments(string text)
    {
        var reader = new Utf8JsonReader(
            System.Text.Encoding.UTF8.GetBytes(text),
            new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow, AllowTrailingCommas = true });
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.Comment)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WriteAsync(string path, JsonObject document, CancellationToken cancellationToken)
    {
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
