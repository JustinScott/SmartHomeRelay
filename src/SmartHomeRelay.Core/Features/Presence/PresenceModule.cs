using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;

namespace SmartHomeRelay.Core.Features.Presence;

/// <summary>
/// Presence (spec 5.1): session lock state, signed-in user, keyboard/mouse idle time, and the derived
/// <c>user_active</c> and <c>presence_state</c>.
/// <list type="bullet">
/// <item>Lock and unlock come from <see cref="ISystemEventSource.SessionSwitched"/> and publish immediately.</item>
/// <item>Idle time is sampled every <see cref="SampleInterval"/>; values are published only when they change.</item>
/// <item>Sampling pauses during sleep (<see cref="IPowerAware"/>) and the lock state is re-read on wake.</item>
/// </list>
/// </summary>
public sealed partial class PresenceModule : IFeatureModule, IPowerAware, IDisposable
{
    /// <summary>How often idle time is checked ("updated every few seconds", spec 5.1).</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

    private readonly ISystemEventSource _events;
    private readonly ISessionStateProvider _session;
    private readonly IIdleTimeProvider _idle;
    private readonly ISettingsState _settings;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly Dictionary<string, string> _published = new(StringComparer.Ordinal);
    private IModuleContext? _context;
    private CancellationTokenSource? _sampling;
    private Task? _samplingLoop;
    private volatile bool _locked;
    private string _userName = string.Empty;

    /// <summary>Creates the module. Nothing runs until <see cref="StartAsync"/>.</summary>
    public PresenceModule(
        ISystemEventSource events,
        ISessionStateProvider session,
        IIdleTimeProvider idle,
        ISettingsState settings,
        TimeProvider time)
    {
        _events = events;
        _session = session;
        _idle = idle;
        _settings = settings;
        _time = time;
    }

    /// <inheritdoc />
    public string Id => "presence";

    /// <inheritdoc />
    public bool IsEnabled(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Features.Presence.Enabled;
    }

    /// <inheritdoc />
    public IReadOnlyList<EntityDescriptor> DescribeEntities() =>
    [
        // No HA "lock" device class: for it, ON means *unlocked*, the opposite of this entity's name.
        new(PresenceKeys.SessionLocked, "Session locked", EntityComponent.BinarySensor) { Icon = "mdi:monitor-lock" },
        new(PresenceKeys.LoggedInUser, "Logged in user", EntityComponent.Sensor) { Icon = "mdi:account" },
        new(PresenceKeys.IdleSeconds, "Idle time", EntityComponent.Sensor)
        {
            DeviceClass = "duration",
            UnitOfMeasurement = "s",
            StateClass = "measurement",
            Icon = "mdi:timer-sand",
        },
        new(PresenceKeys.UserActive, "User active", EntityComponent.BinarySensor) { DeviceClass = "occupancy" },
        new(PresenceKeys.PresenceState, "Presence", EntityComponent.Sensor)
        {
            DeviceClass = "enum",
            Options = PresenceStates.All,
            Icon = "mdi:account-clock",
        },
    ];

    /// <inheritdoc />
    public async Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        // Restarted after being disabled: the host dropped its cached states, so publish everything again.
        _published.Clear();
        _userName = _session.GetUserName();
        _locked = _session.IsLocked();
        _events.SessionSwitched += OnSessionSwitched;

        await PublishAsync(cancellationToken).ConfigureAwait(false);
        StartSampling();
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _events.SessionSwitched -= OnSessionSwitched;
        await StopSamplingAsync().ConfigureAwait(false);
        _context = null;
    }

    /// <inheritdoc />
    public Task OnSuspendingAsync(CancellationToken cancellationToken) => StopSamplingAsync();

    /// <inheritdoc />
    public async Task OnResumedAsync(CancellationToken cancellationToken)
    {
        // Lock events can be missed around sleep (e.g. "require sign-in on wake"), so re-read the truth.
        _locked = _session.IsLocked();
        await PublishAsync(cancellationToken).ConfigureAwait(false);
        StartSampling();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _events.SessionSwitched -= OnSessionSwitched;
        _sampling?.Cancel();
        _sampling?.Dispose();
        _publishGate.Dispose();
    }

    private void OnSessionSwitched(object? sender, SessionSwitchKind kind)
    {
        bool? locked = kind switch
        {
            SessionSwitchKind.Lock => true,
            SessionSwitchKind.Unlock or SessionSwitchKind.Logon => false,
            _ => null,
        };

        if (locked is not { } value || value == _locked)
        {
            return;
        }

        _locked = value;

        // Publish now rather than waiting for the next sample: lock/unlock automations should be instant.
        _ = PublishSafelyAsync();
    }

    private void StartSampling()
    {
        var sampling = new CancellationTokenSource();
        _sampling = sampling;
        _samplingLoop = Task.Run(() => SampleLoopAsync(sampling.Token), CancellationToken.None);
    }

    private async Task StopSamplingAsync()
    {
        var sampling = _sampling;
        var loop = _samplingLoop;
        _sampling = null;
        _samplingLoop = null;
        if (sampling is null)
        {
            return;
        }

        await sampling.CancelAsync().ConfigureAwait(false);
        if (loop is not null)
        {
            await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        sampling.Dispose();
    }

    private async Task SampleLoopAsync(CancellationToken cancellationToken)
    {
        // PeriodicTimer tolerates gaps (Modern Standby throttling): a missed tick simply fires late.
        using var timer = new PeriodicTimer(SampleInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await PublishSafelyAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PublishSafelyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await PublishAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // One bad sample must not stop sampling (spec: a module failure never crashes the app).
            if (_context is { } context)
            {
                LogSampleFailed(context.Logger, ex);
            }
        }
    }

    /// <summary>Samples, then publishes only the values that changed since the last publish.</summary>
    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        if (_context is not { } context)
        {
            return;
        }

        var threshold = TimeSpan.FromSeconds(_settings.Current.Settings.Features.Presence.IdleThresholdSeconds);
        var snapshot = PresenceCalculator.Calculate(_locked, _userName, _idle.GetIdleTime(), threshold);

        await _publishGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var (key, payload) in snapshot.ToPayloads())
            {
                if (_published.TryGetValue(key, out var previous) && previous == payload)
                {
                    continue;
                }

                await context.PublishStateAsync(key, payload, cancellationToken).ConfigureAwait(false);
                _published[key] = payload;
            }
        }
        finally
        {
            _publishGate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Presence sample failed; will retry on the next sample")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
