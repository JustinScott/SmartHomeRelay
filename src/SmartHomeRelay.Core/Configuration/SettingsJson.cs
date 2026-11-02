using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>JSON conventions for <c>settings.json</c>: camelCase keys, enums as names, indented.</summary>
internal static class SettingsJson
{
    /// <summary>Serializer options. Read-only, so sharing one instance is safe.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Matches what the configuration JSON provider accepts, so both agree on what is valid.</summary>
    public static JsonDocumentOptions DocumentOptions { get; } = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
