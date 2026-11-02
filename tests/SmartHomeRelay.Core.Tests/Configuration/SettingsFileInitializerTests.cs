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
    public async Task StartHostedServices_ExistingFile_LeavesItUnchanged()
    {
        const string original = """{ "mqtt": { "host": "broker.local" } }""";
        _paths.WriteSettingsFile(original);
        using var context = new SettingsTestContext(_paths);

        await StartAllAsync(context);

        (await File.ReadAllTextAsync(_paths.SettingsFilePath, TestContext.Current.CancellationToken)).ShouldBe(original);
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
    public void AddRelaySettings_RegistersStateBeforeInitializer()
    {
        using var context = new SettingsTestContext(_paths);

        var hosted = context.Services.GetServices<IHostedService>().ToList();

        hosted.Count.ShouldBe(2);
        hosted[0].ShouldBeOfType<SettingsState>();
        hosted[1].ShouldBeOfType<SettingsFileInitializer>();
    }

    private static async Task StartAllAsync(SettingsTestContext context)
    {
        foreach (var service in context.Services.GetServices<IHostedService>())
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
        }
    }
}
