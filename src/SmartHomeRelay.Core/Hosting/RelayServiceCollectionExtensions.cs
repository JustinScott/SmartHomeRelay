using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Hosting;

/// <summary>DI registration for the MQTT connection and the module host.</summary>
public static class RelayServiceCollectionExtensions
{
    /// <summary>
    /// Registers the transport, reconnect supervisor, <see cref="ModuleHost"/>, and <see cref="LifecycleCoordinator"/>. Feature modules are
    /// registered separately as <see cref="Features.IFeatureModule"/> (one line each, spec 13).
    /// </summary>
    public static IServiceCollection AddRelayMqtt(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMqttTransport, MqttNetTransport>();

        // The settings window's Test connection uses its own short-lived transport (spec 7.1).
        services.AddSingleton(sp => new MqttConnectionTester(() => new MqttNetTransport(), sp.GetRequiredService<Abstractions.ISecretProtector>()));
        services.AddSingleton(_ => new ReconnectBackoff(new Random()));

        // Remove from Home Assistant (spec 4.4): its own short-lived connection, like Test connection.
        services.AddSingleton(sp => new HomeAssistantRemover(
            () => new MqttNetTransport(),
            sp.GetRequiredService<Abstractions.ISecretProtector>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HomeAssistantRemover>>()));

        services.AddSingleton<ModuleHost>();
        services.AddSingleton<IMqttSessionHandler>(sp => sp.GetRequiredService<ModuleHost>());

        services.AddSingleton<MqttConnectionSupervisor>();
        services.AddSingleton<IRelayStatusSource>(sp => sp.GetRequiredService<MqttConnectionSupervisor>());
        services.AddSingleton<IReconnectRequester>(sp => sp.GetRequiredService<MqttConnectionSupervisor>());

        // Start order: modules first, then the connection. Hosts stop in reverse, so the connection
        // publishes offline and closes before modules are torn down.
        services.AddHostedService(sp => sp.GetRequiredService<ModuleHost>());
        services.AddHostedService(sp => sp.GetRequiredService<MqttConnectionSupervisor>());

        // Needs an ISystemEventSource from the platform. Starts last, so suspend/resume events only
        // arrive once the connection and modules are running.
        services.AddHostedService<LifecycleCoordinator>();

        return services;
    }
}
