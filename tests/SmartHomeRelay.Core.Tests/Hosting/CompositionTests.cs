using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Hosting;

/// <summary>
/// Builds the same Core registrations as App/Program.cs (with fake platform services) and validates
/// the whole graph, so a missing registration fails here instead of at app start.
/// </summary>
public sealed class CompositionTests : IDisposable
{
    private readonly TempAppPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void CoreRegistrations_ResolveWithValidation()
    {
        using var configuration = (IDisposable)new ConfigurationBuilder().AddRelaySettingsFile(_paths.SettingsFilePath).Build();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IAppPaths>(_paths);
        services.AddSingleton<IHostNameProvider>(new FakeHostNameProvider("TEST-PC"));
        services.AddSingleton<IShellLauncher>(new FakeShellLauncher());
        services.AddSingleton<IUserNotifier>(new FakeUserNotifier());
        services.AddSingleton<ISecretProtector>(new FakeSecretProtector());
        services.AddSingleton<ISystemEventSource>(new FakeSystemEventSource());
        services.AddSingleton<IHostApplicationLifetime>(new FakeApplicationLifetime());
        services.AddRelaySettings((IConfiguration)configuration);
        services.AddRelayMqtt();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var hosted = provider.GetServices<IHostedService>().Select(h => h.GetType()).ToList();
        hosted.ShouldBe(
        [
            typeof(SettingsState),
            typeof(SettingsFileInitializer),
            typeof(SettingsFileCompleter),
            typeof(PlainPasswordEncryptor),
            typeof(ModuleHost),
            typeof(MqttConnectionSupervisor),
            typeof(LifecycleCoordinator),
        ]);
        provider.GetRequiredService<IRelayStatusSource>().ShouldBeSameAs(provider.GetRequiredService<MqttConnectionSupervisor>());
        provider.GetRequiredService<IReconnectRequester>().ShouldBeSameAs(provider.GetRequiredService<MqttConnectionSupervisor>());
        provider.GetRequiredService<IMqttSessionHandler>().ShouldBeSameAs(provider.GetRequiredService<ModuleHost>());
        provider.GetRequiredService<IMqttTransport>().ShouldBeOfType<MqttNetTransport>();
    }
}
