namespace SmartHomeRelay.Core.Mqtt;

/// <summary>Lets the UI force a fresh connection (tray menu "Reconnect", spec 7.1).</summary>
public interface IReconnectRequester
{
    /// <summary>Publishes <c>offline</c>, disconnects, and connects again immediately (no backoff).</summary>
    void RequestReconnect();
}
