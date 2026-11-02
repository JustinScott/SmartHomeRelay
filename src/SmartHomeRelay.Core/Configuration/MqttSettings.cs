using System.Text.Json.Serialization;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>MQTT broker connection settings (spec 7).</summary>
public sealed class MqttSettings
{
    /// <summary>The default MQTT port for plain TCP.</summary>
    public const int DefaultPort = 1883;

    /// <summary>Broker host name or IP address. Empty means the app is not configured yet.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Broker TCP port.</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>Reserved for a later version. Must be <c>false</c> in v1 (spec 12).</summary>
    public bool UseTls { get; set; }

    /// <summary>Broker user name. Empty for anonymous access.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>The password encrypted with DPAPI (CurrentUser scope), base64 encoded. Never the plain text.</summary>
    public string PasswordProtected { get; set; } = string.Empty;

    /// <summary>
    /// A password the user typed into settings.json by hand (spec 7). Never serialized
    /// (<see cref="JsonIgnoreAttribute"/>): <see cref="JsonSettingsStore"/> always writes it as an empty
    /// <c>"password": ""</c> placeholder so users can see where to type, and <see cref="PlainPasswordEncryptor"/>
    /// moves a typed value into <see cref="PasswordProtected"/> as soon as it is seen. Never log this.
    /// </summary>
    [JsonIgnore]
    public string Password { get; set; } = string.Empty;

    /// <summary>Home Assistant MQTT discovery prefix.</summary>
    public string DiscoveryPrefix { get; set; } = "homeassistant";

    /// <summary>Root of this app's topics, e.g. <c>shr/{deviceId}/status</c>.</summary>
    public string BaseTopic { get; set; } = "shr";

    /// <summary>True when enough is set to attempt a connection.</summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
