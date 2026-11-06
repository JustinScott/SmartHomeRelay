using System.Security.Cryptography;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;

namespace SmartHomeRelay.Core.Mqtt;

/// <summary>
/// Test connection in the settings window (spec 7.1): opens a separate, short connection with the entered
/// settings and closes it cleanly. It uses its own client ID, so the app's real connection is not disturbed.
/// </summary>
public sealed class MqttConnectionTester
{
    /// <summary>How long the broker gets to answer.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly Func<IMqttTransport> _createTransport;
    private readonly ISecretProtector _protector;

    /// <summary>Creates the tester.</summary>
    public MqttConnectionTester(Func<IMqttTransport> createTransport, ISecretProtector protector)
    {
        _createTransport = createTransport;
        _protector = protector;
    }

    /// <summary>
    /// Tests <paramref name="mqtt"/>. The password is <paramref name="typedPassword"/> when given, otherwise the
    /// saved one.
    /// </summary>
    public async Task<ConnectionTestResult> TestAsync(MqttSettings mqtt, DeviceSettings device, string? typedPassword, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mqtt);
        ArgumentNullException.ThrowIfNull(device);

        var host = mqtt.Host.Trim();
        if (host.Length == 0)
        {
            return new(false, "Enter the broker's address.");
        }

        string? password;
        if (!string.IsNullOrEmpty(typedPassword))
        {
            password = typedPassword;
        }
        else if (mqtt.PasswordProtected.Length > 0)
        {
            try
            {
                password = _protector.Unprotect(mqtt.PasswordProtected);
            }
            catch (CryptographicException)
            {
                return new(false, "The saved password can't be read on this PC. Enter it again.");
            }
        }
        else
        {
            password = null;
        }

        var deviceId = device.Id.Trim().Length > 0 ? device.Id.Trim() : "unknown";
        var options = new MqttConnectionOptions
        {
            Host = host,
            Port = mqtt.Port,
            ClientId = $"shr-{deviceId}-test",
            Username = mqtt.Username.Length > 0 ? mqtt.Username : null,
            Password = password,
            WillTopic = $"{mqtt.BaseTopic}/{deviceId}/connection_test",
            ConnectTimeout = Timeout,
        };

        var transport = _createTransport();
        await using (transport.ConfigureAwait(false))
        {
            try
            {
                await transport.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
                await transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                return new(true, $"Connected to {host}:{mqtt.Port}.");
            }
            catch (MqttConnectionRefusedException ex)
            {
                return new(false, $"{host}:{mqtt.Port} refused the connection: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new(false, $"Could not connect to {host}:{mqtt.Port}. Check the address, the port, and that the broker is running.");
            }
        }
    }
}
