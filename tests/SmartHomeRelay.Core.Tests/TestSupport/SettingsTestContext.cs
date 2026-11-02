using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>
/// The real settings registration (<see cref="SettingsServiceCollectionExtensions.AddRelaySettings"/>)
/// over a temp settings file. Write the file before creating the context: reload is asynchronous.
/// </summary>
internal sealed class SettingsTestContext : IDisposable
{
    public const string HostName = "TEST-PC";

    private readonly IConfigurationRoot _configuration;

    public SettingsTestContext(TempAppPaths paths)
    {
        Paths = paths;
        _configuration = new ConfigurationBuilder().AddRelaySettingsFile(paths.SettingsFilePath).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<IHostNameProvider>(new FakeHostNameProvider(HostName));
        services.AddSingleton<IShellLauncher>(Shell);
        services.AddSingleton<IUserNotifier>(Notifier);
        services.AddSingleton<ISecretProtector>(new FakeSecretProtector());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRelaySettings(_configuration);
        Services = services.BuildServiceProvider();
    }

    public TempAppPaths Paths { get; }

    public FakeShellLauncher Shell { get; } = new();

    public FakeUserNotifier Notifier { get; } = new();

    public ServiceProvider Services { get; }

    public SettingsState State => Services.GetRequiredService<SettingsState>();

    public ISettingsStore Store => Services.GetRequiredService<ISettingsStore>();

    public void Dispose()
    {
        Services.Dispose();
        (_configuration as IDisposable)?.Dispose();
    }
}
