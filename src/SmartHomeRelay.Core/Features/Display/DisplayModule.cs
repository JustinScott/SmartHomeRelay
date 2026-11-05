using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Features.Display;

/// <summary>
/// Display (spec 5.3): Monitor off and Monitor on buttons. Windows can't reliably report whether a monitor is
/// on, so there is no state sensor.
/// </summary>
public sealed partial class DisplayModule : IFeatureModule
{
    private readonly IDisplayController _display;
    private readonly IReadOnlyList<EntityDescriptor> _entities;
    private IModuleContext? _context;

    /// <summary>Creates the module.</summary>
    public DisplayModule(IDisplayController display)
    {
        _display = display;
        _entities =
        [
            Button(DisplayKeys.MonitorOff, "Monitor off", "mdi:monitor-off", ct => _display.TurnOffAsync(ct)),
            Button(DisplayKeys.MonitorOn, "Monitor on", "mdi:monitor", ct => _display.TurnOnAsync(ct)),
        ];
    }

    /// <inheritdoc />
    public string Id => "display";

    /// <inheritdoc />
    public bool IsEnabled(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Features.Display.Enabled;
    }

    /// <inheritdoc />
    public IReadOnlyList<EntityDescriptor> DescribeEntities() => _entities;

    /// <inheritdoc />
    public Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _context = null;
        return Task.CompletedTask;
    }

    private EntityDescriptor Button(string key, string name, string icon, Func<CancellationToken, Task> action) =>
        new(key, name, EntityComponent.Button)
        {
            Icon = icon,
            CommandHandler = async (payload, ct) =>
            {
                if (!string.Equals(payload, DiscoveryPayloadBuilder.ButtonPressPayload, StringComparison.OrdinalIgnoreCase))
                {
                    if (_context?.Logger is { } logger)
                    {
                        LogUnexpectedPayload(logger, key);
                    }

                    return;
                }

                if (_context?.Logger is { } log)
                {
                    LogCommand(log, key);
                }

                await action(ct).ConfigureAwait(false);
            },
        };

    [LoggerMessage(Level = LogLevel.Information, Message = "Display command {EntityKey} received from Home Assistant")]
    private static partial void LogCommand(ILogger logger, string entityKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored {EntityKey}: the payload was not PRESS")]
    private static partial void LogUnexpectedPayload(ILogger logger, string entityKey);
}
