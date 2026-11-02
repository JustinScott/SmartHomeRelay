using System.Text.Json;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Configuration;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempAppPaths _paths = new();
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreTests()
    {
        _store = new JsonSettingsStore(_paths);
    }

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void Exists_NoFile_ReturnsFalse()
    {
        _store.Exists().ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_MissingFolder_CreatesFolderAndFile()
    {
        await _store.SaveAsync(TestSettings.Valid(), TestContext.Current.CancellationToken);

        _store.Exists().ShouldBeTrue();
        File.Exists(_paths.SettingsFilePath + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_WritesSpecKeyNames()
    {
        // Key names are the documented file format (spec 7). Renaming one silently resets a user's setting.
        await _store.SaveAsync(TestSettings.Valid(), TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_paths.SettingsFilePath, TestContext.Current.CancellationToken));
        var root = document.RootElement;

        PropertyNames(root).ShouldBe(["mqtt", "device", "features", "startWithWindows", "logLevel"], ignoreOrder: true);
        PropertyNames(root.GetProperty("mqtt")).ShouldBe(
            ["host", "port", "useTls", "username", "passwordProtected", "discoveryPrefix", "baseTopic"],
            ignoreOrder: true);
        PropertyNames(root.GetProperty("device")).ShouldBe(["id", "name"], ignoreOrder: true);
        PropertyNames(root.GetProperty("features")).ShouldBe(
            ["presence", "power", "display", "audio", "metrics", "notifications"],
            ignoreOrder: true);
        PropertyNames(root.GetProperty("features").GetProperty("presence")).ShouldBe(["enabled", "idleThresholdSeconds"], ignoreOrder: true);
        PropertyNames(root.GetProperty("features").GetProperty("power")).ShouldBe(
            ["enabled", "allowShutdown", "allowRestart", "allowHibernate", "allowSignOut", "shutdownDelaySeconds"],
            ignoreOrder: true);
        PropertyNames(root.GetProperty("features").GetProperty("metrics")).ShouldBe(["enabled", "intervalSeconds"], ignoreOrder: true);
        PropertyNames(root.GetProperty("features").GetProperty("notifications")).ShouldBe(
            ["enabled", "imageDownloadTimeoutSeconds", "imageMaxBytes"],
            ignoreOrder: true);
        root.GetProperty("logLevel").GetString().ShouldBe("Information");
    }

    [Fact]
    public async Task SaveAsync_DestructivePowerCommands_AreDisabledByDefault()
    {
        await _store.SaveAsync(TestSettings.Valid(), TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_paths.SettingsFilePath, TestContext.Current.CancellationToken));
        var power = document.RootElement.GetProperty("features").GetProperty("power");
        power.GetProperty("allowShutdown").GetBoolean().ShouldBeFalse();
        power.GetProperty("allowRestart").GetBoolean().ShouldBeFalse();
        power.GetProperty("allowHibernate").GetBoolean().ShouldBeFalse();
        power.GetProperty("allowSignOut").GetBoolean().ShouldBeFalse();
        power.GetProperty("shutdownDelaySeconds").GetInt32().ShouldBe(60);
    }

    [Fact]
    public async Task SaveAsync_ExistingFile_ReplacesIt()
    {
        _paths.WriteSettingsFile("""{ "previousContent": true }""");
        var settings = TestSettings.Valid();
        settings.Mqtt.Host = "broker.local";

        await _store.SaveAsync(settings, TestContext.Current.CancellationToken);

        var text = await File.ReadAllTextAsync(_paths.SettingsFilePath, TestContext.Current.CancellationToken);
        text.ShouldContain("broker.local");
        text.ShouldNotContain("previousContent");
    }

    [Fact]
    public void GetSyntaxError_NoFile_ReturnsNull()
    {
        _store.GetSyntaxError().ShouldBeNull();
    }

    [Fact]
    public void GetSyntaxError_ValidJsonWithCommentsAndTrailingCommas_ReturnsNull()
    {
        // The configuration provider accepts these, so the store must agree.
        _paths.WriteSettingsFile("""
            {
              // a comment
              "mqtt": { "host": "broker.local", },
            }
            """);

        _store.GetSyntaxError().ShouldBeNull();
    }

    [Fact]
    public void GetSyntaxError_MalformedJson_ReturnsMessageWithLine()
    {
        _paths.WriteSettingsFile("{\n  \"mqtt\": {\n    \"host\": \n}");

        var error = _store.GetSyntaxError();

        error.ShouldNotBeNull();
        error.ShouldStartWith("settings.json is not valid JSON (line ");
    }

    [Fact]
    public void GetSyntaxError_RootIsNotAnObject_ReturnsMessage()
    {
        _paths.WriteSettingsFile("[1, 2, 3]");

        _store.GetSyntaxError().ShouldBe("settings.json must contain a JSON object.");
    }

    private static string[] PropertyNames(JsonElement element) =>
        [.. element.EnumerateObject().Select(p => p.Name)];
}
