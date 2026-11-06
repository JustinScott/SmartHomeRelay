using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>DI registration for settings.</summary>
public static class SettingsServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="AppSettings"/> to <paramref name="configuration"/> and registers validation,
    /// the store, <see cref="ISettingsState"/>, and first-run file creation.
    /// Requires the platform's <see cref="Abstractions.IHostNameProvider"/>, <see cref="Abstractions.IAppPaths"/>,
    /// <see cref="Abstractions.ISettingsWindow"/>, <see cref="Abstractions.IUserNotifier"/>, and
    /// <see cref="Abstractions.ISecretProtector"/>.
    /// </summary>
    public static IServiceCollection AddRelaySettings(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AppSettings>().Bind(configuration);
        services.AddSingleton<DeviceDefaultsPostConfigure>();
        services.AddSingleton<IPostConfigureOptions<AppSettings>>(sp => sp.GetRequiredService<DeviceDefaultsPostConfigure>());
        services.AddSingleton<SettingsValidator>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<SettingsEditor>();
        services.AddSingleton<SettingsState>();
        services.AddSingleton<ISettingsState>(sp => sp.GetRequiredService<SettingsState>());

        // Order matters: the state is evaluated before the initializer decides whether to write defaults.
        services.AddHostedService(sp => sp.GetRequiredService<SettingsState>());
        services.AddHostedService<SettingsFileInitializer>();
        services.AddHostedService<SettingsFileCompleter>();
        services.AddHostedService<PlainPasswordEncryptor>();

        return services;
    }
}
