using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Configuration;

public sealed class PlainPasswordEncryptorTests : IDisposable
{
    private readonly TempAppPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    [Fact]
    public async Task Start_PlainPassword_IsEncryptedAndRemovedFromFile()
    {
        _paths.WriteSettingsFile("""
            {
              "mqtt": { "host": "broker.local", "username": "relay", "password": "hunter2" },
              "device": { "id": "DESKTOP-ABC123", "name": "Desk PC" }
            }
            """);
        using var context = new SettingsTestContext(_paths);
        await context.State.StartAsync(TestContext.Current.CancellationToken);

        await Encryptor(context).StartAsync(TestContext.Current.CancellationToken);

        var text = await File.ReadAllTextAsync(_paths.SettingsFilePath, TestContext.Current.CancellationToken);
        text.ShouldNotContain("hunter2");

        using var document = JsonDocument.Parse(text);
        var mqtt = document.RootElement.GetProperty("mqtt");
        new FakeSecretProtector().Unprotect(mqtt.GetProperty("passwordProtected").GetString()!).ShouldBe("hunter2");
        mqtt.GetProperty("password").GetString().ShouldBe(string.Empty);
        mqtt.GetProperty("host").GetString().ShouldBe("broker.local");
        mqtt.GetProperty("username").GetString().ShouldBe("relay");
        document.RootElement.GetProperty("device").GetProperty("id").GetString().ShouldBe("DESKTOP-ABC123");
    }

    [Fact]
    public async Task Start_NoPlainPassword_LeavesFileUntouched()
    {
        const string original = """{ "mqtt": { "host": "broker.local", "passwordProtected": "enc:c2VjcmV0" } }""";
        _paths.WriteSettingsFile(original);
        using var context = new SettingsTestContext(_paths);
        await context.State.StartAsync(TestContext.Current.CancellationToken);

        await Encryptor(context).StartAsync(TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(_paths.SettingsFilePath, TestContext.Current.CancellationToken)).ShouldBe(original);
    }

    [Fact]
    public async Task Start_PlainPasswordReplacesOldProtectedValue()
    {
        _paths.WriteSettingsFile("""{ "mqtt": { "host": "broker.local", "password": "new", "passwordProtected": "enc:b2xk" } }""");
        using var context = new SettingsTestContext(_paths);
        await context.State.StartAsync(TestContext.Current.CancellationToken);

        await Encryptor(context).StartAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_paths.SettingsFilePath, TestContext.Current.CancellationToken));
        new FakeSecretProtector().Unprotect(document.RootElement.GetProperty("mqtt").GetProperty("passwordProtected").GetString()!).ShouldBe("new");
    }

    [Fact]
    public void Serialize_Settings_NeverWritesPlainPassword()
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.Password = "hunter2";

        JsonSerializer.Serialize(settings).ShouldNotContain("hunter2");
    }

    private static PlainPasswordEncryptor Encryptor(SettingsTestContext context) =>
        context.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<PlainPasswordEncryptor>().Single();
}
