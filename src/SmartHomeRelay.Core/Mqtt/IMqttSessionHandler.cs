using SmartHomeRelay.Core.Configuration;

namespace SmartHomeRelay.Core.Mqtt;

/// <summary>Called by <see cref="MqttConnectionSupervisor"/> after each successful (re)connect.</summary>
public interface IMqttSessionHandler
{
    /// <summary>
    /// Subscribes, publishes discovery, and publishes full state for the new session. <paramref name="settings"/>
    /// are exactly the settings the connection was made with.
    /// </summary>
    Task OnConnectedAsync(MqttTopics topics, AppSettings settings, CancellationToken cancellationToken);
}
