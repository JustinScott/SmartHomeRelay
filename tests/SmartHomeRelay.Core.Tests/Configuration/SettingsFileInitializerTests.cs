using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Configuration;

public sealed class SettingsFileInitializerTests : IDisposable
{
    private readonly TempAppPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    [Fact]
    public async Task StartHostedServices_NoFile_CreatesFileWithHostNamePinnedAsDeviceId()
    {
        using var context = new SettingsTestContext(_paths);

        await StartAllAsync(context);

        File.Exists(_paths.SettingsFilePath).ShouldBeTrue();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_paths.SettingsFilePath, TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("device").GetProperty("id").GetString().ShouldBe(SettingsTestContext.HostName);
        document.RootElement.GetProperty("mqtt").GetProperty("host").GetString().ShouldBeEmpty();
    }

    [Fact]
    public async Task StartHostedServices_ExistingFile_KeepsItsValues_AndIsNotReplacedWithDefaults()
    {
        // The initializer never overwrites an existing file. (The completer may add missing settings; spec 7.)
        const string original = """{ "mqtt": { "host": "broker.local", "port": 1884 } }""";
        _paths.WriteSettingsFile(original);
        using var context = new SettingsTestContext(_paths);

        await StartAllAsync(context);

        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(_paths.SettingsFilePath, TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("mqtt").GetProperty("host").GetString().ShouldBe("broker.local");
        document.RootElement.GetProperty("mqtt").GetProperty("port").GetInt32().ShouldBe(1884);
        context.Window.Opened.ShouldBe(0, "the welcome flow is for first run only");
    }

    [Fact]
    public async Task StartHostedServices_MalformedFile_IsNotOverwritten()
    {
        // The user's file is kept so they can fix it; the app runs on defaults meanwhile.
        const string original = "{ broken";
        _paths.WriteSettingsFile(original);
        using var context = new SettingsTestContext(_paths);

        await StartAllAsync(context);

        (await File.ReadAllTextAsync(_paths.SettingsFilePath, TestContext.Current.CancellationToken)).ShouldBe(original);
    }

    [Fact]
    public async Task StartHostedServices_NoFile_OpensTheSettingsWindowAndShowsWelcome()
    {
        using var context = new SettingsTestContext(_paths);

        await StartAllAsync(context);

        context.Window.Opened.ShouldBe(1);
        context.Notifier.Notifications.Count.ShouldBe(1);
        context.Notifier.Notifications[0].Title.ShouldBe(SettingsFileInitializer.WelcomeTitle);
    }

    [Fact]
    public async Task StartHostedServices_ExistingFile_DoesNotOpenItOrNotify()
    {
        _paths.WriteSettingsFile("""{ "mqtt": { "host": "broker.local" } }""");
        using var context = new SettingsTestContext(_paths);

        await StartAllAsync(context);

        context.Window.Opened.ShouldBe(0);
        context.Notifier.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public void AddRelaySettings_RegistersStateBeforeInitializerCompleterAndEncryptor()
    {
        using var context = new SettingsTestContext(_paths);

        var hosted = context.Services.GetServices<IHostedService>().ToList();

        hosted.Count.ShouldBe(4);
        hosted[0].ShouldBeOfType<SettingsState>();
        hosted[1].ShouldBeOfType<SettingsFileInitializer>();
        hosted[2].ShouldBeOfType<SettingsFileCompleter>();
        hosted[3].ShouldBeOfType<PlainPasswordEncryptor>();
    }

    private static async Task StartAllAsync(SettingsTestContext context)
    {
        foreach (var service in context.Services.GetServices<IHostedService>())
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
        }
    }
}
