using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Hosting;

/// <summary>DI registration for the MQTT connection and the module host.</summary>
public static class RelayServiceCollectionExtensions
{
    /// <summary>
    /// Registers the transport, reconnect supervisor, and <see cref="ModuleHost"/>. Feature modules are
    /// registered separately as <see cref="Features.IFeatureModule"/> (one line each, spec 13).
    /// </summary>
    public static IServiceCollection AddRelayMqtt(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMqttTransport, MqttNetTransport>();
        services.AddSingleton(_ => new ReconnectBackoff(new Random()));

        services.AddSingleton<ModuleHost>();
        services.AddSingleton<IMqttSessionHandler>(sp => sp.GetRequiredService<ModuleHost>());

        services.AddSingleton<MqttConnectionSupervisor>();
        services.AddSingleton<IRelayStatusSource>(sp => sp.GetRequiredService<MqttConnectionSupervisor>());
        services.AddSingleton<IReconnectRequester>(sp => sp.GetRequiredService<MqttConnectionSupervisor>());

        // Start order: modules first, then the connection. Hosts stop in reverse, so the connection
        // publishes offline and closes before modules are torn down.
        services.AddHostedService(sp => sp.GetRequiredService<ModuleHost>());
        services.AddHostedService(sp => sp.GetRequiredService<MqttConnectionSupervisor>());

        return services;
    }
}
