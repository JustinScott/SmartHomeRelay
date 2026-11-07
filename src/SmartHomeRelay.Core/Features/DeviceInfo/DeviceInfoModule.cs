using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Features.DeviceInfo;

/// <summary>
/// About the PC and the app itself rather than a feature (spec 4.3), so always on:
/// <list type="bullet">
/// <item><c>connected</c>: on while Smart Home Relay is connected, off while the PC is offline (asleep, shut down,
/// or the app closed). It reads the availability topic, so nothing publishes it.</item>
/// <item><c>version</c>: which build is running (<see cref="RelayVersion.Current"/>), published once at start.</item>
/// </list>
/// </summary>
public sealed class DeviceInfoModule : IFeatureModule
{
    /// <summary>The Connected entity key. Never rename it.</summary>
    public const string ConnectedKey = "connected";

    /// <summary>The Version entity key. Never rename it.</summary>
    public const string VersionKey = "version";

    private static readonly IReadOnlyList<EntityDescriptor> s_entities =
    [
        new(ConnectedKey, "Connected", EntityComponent.BinarySensor)
        {
            DeviceClass = "connectivity",
            ReportsConnection = true,
        },
        new(VersionKey, "Version", EntityComponent.Sensor)
        {
            Icon = "mdi:information-outline",
            Category = EntityCategory.Diagnostic,
        },
    ];

    private readonly string _version;

    /// <summary>Creates the module for the running build.</summary>
    public DeviceInfoModule()
        : this(RelayVersion.Current)
    {
    }

    /// <summary>Creates the module reporting <paramref name="version"/>. For tests.</summary>
    internal DeviceInfoModule(string version)
    {
        _version = version;
    }

    /// <inheritdoc />
    public string Id => "device_info";

    /// <inheritdoc />
    public bool IsEnabled(AppSettings settings) => true;

    /// <inheritdoc />
    public IReadOnlyList<EntityDescriptor> DescribeEntities() => s_entities;

    /// <inheritdoc />
    public Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.PublishStateAsync(VersionKey, _version, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
