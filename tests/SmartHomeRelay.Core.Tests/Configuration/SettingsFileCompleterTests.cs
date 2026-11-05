using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Configuration;

/// <summary>Spec 7: settings added in a newer version appear in an existing settings.json.</summary>
public sealed class SettingsFileCompleterTests : IDisposable
{
    // A file as milestone 5 wrote it: no features.hardware, plus a key this version doesn't know.
    private const string OldFile = """
        {
          "mqtt": { "host": "broker.local", "port": 1884, "useTls": false, "username": "relay", "password": "", "passwordProtected": "enc:c2VjcmV0", "discoveryPrefix": "homeassistant", "baseTopic": "shr" },
          "device": { "id": "BOX1", "name": "Box One" },
          "features": {
            "presence": { "enabled": true, "idleThresholdSeconds": 120 },
            "power": { "enabled": true, "allowShutdown": true, "allowRestart": false, "allowHibernate": false, "allowSignOut": false, "shutdownDelaySeconds": 60 },
            "display": { "enabled": true },
            "audio": { "enabled": true },
            "metrics": { "enabled": true, "intervalSeconds": 15 },
            "notifications": { "enabled": true, "imageDownloadTimeoutSeconds": 10, "imageMaxBytes": 10485760 }
          },
          "startWithWindows": true,
          "logLevel": "Debug",
          "myNote": "kept"
        }
        """;

    private readonly TempAppPaths _paths = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _paths.Dispose();

    [Fact]
    public async Task OldFile_GetsTheHardwareSection_WithDefaults()
    {
        var root = await CompleteAsync(OldFile);

        var hardware = root["features"]!["hardware"]!.AsObject();
        hardware["enabled"]!.GetValue<bool>().ShouldBeFalse();
        hardware["port"]!.GetValue<int>().ShouldBe(8085);
        hardware["password"]!.GetValue<string>().ShouldBe(string.Empty, "the placeholder shows where to type");
        hardware["sensors"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["cpu_temperature", "cpu_fan", "ssd_temperature"]);
        Keys(root["features"]!).ShouldBe(["presence", "power", "display", "audio", "metrics", "notifications", "hardware", "mediaPlayer"]);
    }

    [Fact]
    public async Task OldFile_GetsTheMediaPlayerSection_TurnedOff()
    {
        var root = await CompleteAsync(OldFile);

        var media = root["features"]!["mediaPlayer"]!.AsObject();
        media["enabled"]!.GetValue<bool>().ShouldBeFalse();
        media["interruption"]!.GetValue<string>().ShouldBe("duck");
        media["duckLevelPercent"]!.GetValue<int>().ShouldBe(20);
        media["positionIntervalSeconds"]!.GetValue<int>().ShouldBe(5);
    }

    [Fact]
    public async Task OldFile_ValuesOrderAndUnknownKeys_AreKept()
    {
        var root = await CompleteAsync(OldFile);

        root["mqtt"]!["port"]!.GetValue<int>().ShouldBe(1884);
        root["mqtt"]!["passwordProtected"]!.GetValue<string>().ShouldBe("enc:c2VjcmV0");
        root["device"]!["id"]!.GetValue<string>().ShouldBe("BOX1");
        root["features"]!["power"]!["allowShutdown"]!.GetValue<bool>().ShouldBeTrue();
        root["features"]!["metrics"]!["intervalSeconds"]!.GetValue<int>().ShouldBe(15);
        root["logLevel"]!.GetValue<string>().ShouldBe("Debug");
        root["myNote"]!.GetValue<string>().ShouldBe("kept");
        Keys(root).ShouldBe(["mqtt", "device", "features", "startWithWindows", "logLevel", "myNote"]);
    }

    [Fact]
    public async Task CompleteFile_IsNotRewritten()
    {
        await CompleteAsync(OldFile);
        var once = await File.ReadAllTextAsync(_paths.SettingsFilePath, Ct);
        var written = File.GetLastWriteTimeUtc(_paths.SettingsFilePath);

        await RunCompleterAsync();

        (await File.ReadAllTextAsync(_paths.SettingsFilePath, Ct)).ShouldBe(once);
        File.GetLastWriteTimeUtc(_paths.SettingsFilePath).ShouldBe(written);
    }

    [Fact]
    public async Task KeysMatchCaseInsensitively_LikeConfigurationBinding()
    {
        var root = await CompleteAsync("""
            { "MQTT": { "Host": "broker.local" }, "device": { "id": "BOX1", "name": "Box" }, "features": { "Hardware": { "Enabled": true } } }
            """);

        root.AsObject().ContainsKey("mqtt").ShouldBeFalse();
        root["MQTT"]!.AsObject().ContainsKey("host").ShouldBeFalse();
        root["MQTT"]!["port"]!.GetValue<int>().ShouldBe(1883);
        root["features"]!.AsObject().ContainsKey("hardware").ShouldBeFalse();
        root["features"]!["Hardware"]!["Enabled"]!.GetValue<bool>().ShouldBeTrue();
        root["features"]!["Hardware"]!["port"]!.GetValue<int>().ShouldBe(8085);
    }

    [Fact]
    public async Task MissingKey_GoesAfterItsPredecessor_EvenWhenTheFileIsReordered()
    {
        var root = await CompleteAsync("""
            { "device": { "id": "BOX1", "name": "Box" }, "mqtt": { "baseTopic": "shr", "host": "broker.local" } }
            """);

        // Defaults order: host, port, useTls, username, password, passwordProtected, discoveryPrefix, baseTopic.
        Keys(root["mqtt"]!).ShouldBe(["baseTopic", "host", "port", "useTls", "username", "password", "passwordProtected", "discoveryPrefix"]);
    }

    [Fact]
    public async Task FileWithComments_IsLeftAlone()
    {
        var withComment = OldFile.Replace("\"logLevel\": \"Debug\",", "// verbose for now\n  \"logLevel\": \"Debug\",", StringComparison.Ordinal);

        await CompleteAsync(withComment);

        (await File.ReadAllTextAsync(_paths.SettingsFilePath, Ct)).ShouldBe(withComment);
    }

    [Fact]
    public async Task InvalidSettings_AreNeverRewritten()
    {
        // Spec 7: a file with problems is the user's to fix.
        var invalid = OldFile.Replace("\"port\": 1884", "\"port\": 99999", StringComparison.Ordinal);

        await CompleteAsync(invalid);

        (await File.ReadAllTextAsync(_paths.SettingsFilePath, Ct)).ShouldBe(invalid);
    }

    [Fact]
    public async Task BrokenJson_IsNeverRewritten()
    {
        const string broken = """{ "mqtt": { "host": "broker.local" """;

        await CompleteAsync(broken);

        (await File.ReadAllTextAsync(_paths.SettingsFilePath, Ct)).ShouldBe(broken);
    }

    private async Task<JsonNode> CompleteAsync(string contents)
    {
        _paths.WriteSettingsFile(contents);
        return await RunCompleterAsync();
    }

    private async Task<JsonNode> RunCompleterAsync()
    {
        using var context = new SettingsTestContext(_paths);
        await context.State.StartAsync(Ct);

        await context.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<SettingsFileCompleter>().Single().StartAsync(Ct);

        var text = await File.ReadAllTextAsync(_paths.SettingsFilePath, Ct);
        try
        {
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static List<string> Keys(JsonNode node) => [.. node.AsObject().Select(p => p.Key)];
}
