using System.Globalization;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Features.Power;

/// <summary>
/// Power (spec 5.2): shutdown, restart, cancel, sleep, hibernate, lock, and sign-out buttons, plus the
/// last boot time and uptime.
/// <list type="bullet">
/// <item>Shutdown, restart, hibernate, and sign-out only run if allowed in settings (off by default). The
/// buttons always appear in HA; a press that isn't allowed is ignored and logged.</item>
/// <item>Shutdown and restart wait <c>shutdownDelaySeconds</c> with Windows' countdown; Cancel shutdown aborts.</item>
/// <item>Uptime is published every <see cref="UptimeInterval"/>; last boot once.</item>
/// </list>
/// </summary>
public sealed partial class PowerModule : IFeatureModule, IPowerAware, IDisposable
{
    /// <summary>How often uptime is published (each update is a history row in HA).</summary>
    public static readonly TimeSpan UptimeInterval = TimeSpan.FromMinutes(5);

    private readonly IPowerController _power;
    private readonly IUptimeProvider _uptime;
    private readonly ISettingsState _settings;
    private readonly TimeProvider _time;
    private readonly bool _offerHibernate;
    private readonly IReadOnlyList<EntityDescriptor> _entities;
    private IModuleContext? _context;
    private CancellationTokenSource? _publishing;
    private Task? _publishLoop;

    /// <summary>Creates the module. Whether Hibernate is offered is decided here, once.</summary>
    public PowerModule(IPowerController power, IUptimeProvider uptime, ISettingsState settings, TimeProvider time)
    {
        _power = power;
        _uptime = uptime;
        _settings = settings;
        _time = time;

        // Read once so DescribeEntities returns the same keys every time (module contract).
        _offerHibernate = power.IsHibernateAvailable;
        _entities = BuildEntities();
    }

    /// <inheritdoc />
    public string Id => "power";

    /// <inheritdoc />
    public bool IsEnabled(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Features.Power.Enabled;
    }

    /// <inheritdoc />
    public IReadOnlyList<EntityDescriptor> DescribeEntities() => _entities;

    /// <inheritdoc />
    public async Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        var bootTime = (_time.GetUtcNow() - _uptime.GetUptime()).ToUniversalTime();
        await context.PublishStateAsync(PowerKeys.LastBoot, FormatTimestamp(bootTime), cancellationToken).ConfigureAwait(false);
        await PublishUptimeAsync(cancellationToken).ConfigureAwait(false);
        StartPublishing();
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopPublishingAsync().ConfigureAwait(false);
        _context = null;
    }

    /// <inheritdoc />
    public Task OnSuspendingAsync(CancellationToken cancellationToken) => StopPublishingAsync();

    /// <inheritdoc />
    public async Task OnResumedAsync(CancellationToken cancellationToken)
    {
        await PublishUptimeAsync(cancellationToken).ConfigureAwait(false);
        StartPublishing();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _publishing?.Cancel();
        _publishing?.Dispose();
    }

    /// <summary>
    /// Hours since boot, rounded down to one decimal (6 minutes), e.g. <c>13.6</c>. Hours read naturally in
    /// HA; finer precision is noise at a 5-minute publish interval.
    /// </summary>
    internal static string FormatUptime(TimeSpan uptime)
    {
        var tenths = Math.Max(0, (long)(uptime.Ticks / (TimeSpan.TicksPerHour / 10)));
        return (tenths / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>ISO 8601 in UTC, to the second, which HA parses for timestamp sensors.</summary>
    internal static string FormatTimestamp(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

    private List<EntityDescriptor> BuildEntities()
    {
        var entities = new List<EntityDescriptor>
        {
            Button(PowerKeys.Shutdown, "Shutdown", "mdi:power", ShutdownAsync),
            Button(PowerKeys.Restart, "Restart", "mdi:restart", RestartAsync) with { DeviceClass = "restart" },
            Button(PowerKeys.CancelShutdown, "Cancel shutdown", "mdi:cancel", ct => _power.CancelShutdownAsync(ct)),
            Button(PowerKeys.Sleep, "Sleep", "mdi:power-sleep", ct => _power.SleepAsync(ct)),
        };

        if (_offerHibernate)
        {
            entities.Add(Button(PowerKeys.Hibernate, "Hibernate", "mdi:snowflake", HibernateAsync));
        }

        entities.Add(Button(PowerKeys.Lock, "Lock", "mdi:lock", ct => _power.LockAsync(ct)));
        entities.Add(Button(PowerKeys.SignOut, "Sign out", "mdi:logout", SignOutAsync));
        entities.Add(new EntityDescriptor(PowerKeys.LastBoot, "Last boot", EntityComponent.Sensor)
        {
            DeviceClass = "timestamp",
            Category = EntityCategory.Diagnostic,
        });
        entities.Add(new EntityDescriptor(PowerKeys.Uptime, "Uptime", EntityComponent.Sensor)
        {
            DeviceClass = "duration",
            UnitOfMeasurement = "h",
            Icon = "mdi:clock-outline",
            Category = EntityCategory.Diagnostic,
        });
        return entities;
    }

    private EntityDescriptor Button(string key, string name, string icon, Func<CancellationToken, Task> action) =>
        new(key, name, EntityComponent.Button)
        {
            Icon = icon,
            CommandHandler = async (payload, ct) =>
            {
                // HA sends PRESS. Anything else came from somewhere else; a power action needs a real press.
                if (!string.Equals(payload, DiscoveryPayloadBuilder.ButtonPressPayload, StringComparison.OrdinalIgnoreCase))
                {
                    Log(l => LogUnexpectedPayload(l, key));
                    return;
                }

                Log(l => LogCommand(l, key));
                await action(ct).ConfigureAwait(false);
            },
        };

    private Task ShutdownAsync(CancellationToken ct) =>
        IfAllowed(PowerKeys.Shutdown, "features.power.allowShutdown", p => p.AllowShutdown, p => _power.ShutdownAsync(Delay(p), ct));

    private Task RestartAsync(CancellationToken ct) =>
        IfAllowed(PowerKeys.Restart, "features.power.allowRestart", p => p.AllowRestart, p => _power.RestartAsync(Delay(p), ct));

    private Task HibernateAsync(CancellationToken ct) =>
        IfAllowed(PowerKeys.Hibernate, "features.power.allowHibernate", p => p.AllowHibernate, _ => _power.HibernateAsync(ct));

    private Task SignOutAsync(CancellationToken ct) =>
        IfAllowed(PowerKeys.SignOut, "features.power.allowSignOut", p => p.AllowSignOut, _ => _power.SignOutAsync(ct));

    /// <summary>
    /// Runs a destructive action only if allowed in the current settings. Invalid settings never allow one:
    /// when the file is broken the app runs on defaults, where these are all off.
    /// </summary>
    private Task IfAllowed(string key, string setting, Func<PowerSettings, bool> allowed, Func<PowerSettings, Task> action)
    {
        var snapshot = _settings.Current;
        var power = snapshot.Settings.Features.Power;
        if (!snapshot.IsValid || !allowed(power))
        {
            Log(l => LogNotAllowed(l, key, setting));
            return Task.CompletedTask;
        }

        return action(power);
    }

    private static TimeSpan Delay(PowerSettings power) => TimeSpan.FromSeconds(power.ShutdownDelaySeconds);

    private void StartPublishing()
    {
        var publishing = new CancellationTokenSource();
        _publishing = publishing;
        _publishLoop = Task.Run(() => PublishLoopAsync(publishing.Token), CancellationToken.None);
    }

    private async Task StopPublishingAsync()
    {
        var publishing = _publishing;
        var loop = _publishLoop;
        _publishing = null;
        _publishLoop = null;
        if (publishing is null)
        {
            return;
        }

        await publishing.CancelAsync().ConfigureAwait(false);
        if (loop is not null)
        {
            await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        publishing.Dispose();
    }

    private async Task PublishLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(UptimeInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await PublishUptimeAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log(l => LogUptimeFailed(l, ex));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private Task PublishUptimeAsync(CancellationToken cancellationToken) =>
        _context?.PublishStateAsync(PowerKeys.Uptime, FormatUptime(_uptime.GetUptime()), cancellationToken) ?? Task.CompletedTask;

    private void Log(Action<ILogger> write)
    {
        if (_context?.Logger is { } logger)
        {
            write(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Power command {EntityKey} received from Home Assistant")]
    private static partial void LogCommand(ILogger logger, string entityKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored {EntityKey}: not allowed. Set {Setting} to true in settings.json to enable it")]
    private static partial void LogNotAllowed(ILogger logger, string entityKey, string setting);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored {EntityKey}: the payload was not PRESS")]
    private static partial void LogUnexpectedPayload(ILogger logger, string entityKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish uptime; will retry on the next interval")]
    private static partial void LogUptimeFailed(ILogger logger, Exception exception);
}
