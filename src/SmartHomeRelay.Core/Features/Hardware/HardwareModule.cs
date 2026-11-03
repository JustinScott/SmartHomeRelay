using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;

namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>
/// Hardware sensors from a user-managed LibreHardwareMonitor (spec 5.8).
/// <list type="bullet">
/// <item>Each sample: anti-spoofing checks, then <c>GET 127.0.0.1:{port}/data.json</c>, then validation.
/// Only a trusted, understood reading is published; otherwise <c>hardware_status</c> says why and the
/// sensors go unavailable (their availability follows <c>hardware_status</c>).</item>
/// <item>The tray shows one balloon per new problem, never one per sample.</item>
/// <item>Which sensors exist is read from settings at startup, so <see cref="DescribeEntities"/> is stable.</item>
/// </list>
/// </summary>
public sealed partial class HardwareModule : IFeatureModule, IPowerAware, IDisposable
{
    /// <summary>Delay before the first sample after start or wake.</summary>
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long "not running" is tolerated after start before the user is told: LHM and this app usually
    /// start at the same sign-in, and LHM may come up a little later.
    /// </summary>
    public static readonly TimeSpan NotRunningGrace = TimeSpan.FromMinutes(2);

    /// <summary>The MQTT payload HA reads as "unknown" for a sensor.</summary>
    public const string UnknownPayload = "None";

    private const string NotifyTitle = "Hardware sensors";

    private readonly ILibreHardwareMonitorEndpoint _endpoint;
    private readonly ISettingsState _settings;
    private readonly ISecretProtector _protector;
    private readonly IUserNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<HardwareSensorDefinition> _sensors;
    private readonly IReadOnlyList<EntityDescriptor> _entities;
    private readonly Dictionary<string, double?> _published = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warnedUnmatched = new(StringComparer.Ordinal);
    private IModuleContext? _context;
    private CancellationTokenSource? _sampling;
    private Task? _samplingLoop;
    private long _startedAt;
    private HardwareStatus? _status;
    private string? _versionPayload;
    private HardwareStatus? _notified;

    /// <summary>Creates the module. The sensor list is read from settings once, here.</summary>
    public HardwareModule(
        ILibreHardwareMonitorEndpoint endpoint,
        ISettingsState settings,
        ISecretProtector protector,
        IUserNotifier notifier,
        TimeProvider time)
    {
        _endpoint = endpoint;
        _settings = settings;
        _protector = protector;
        _notifier = notifier;
        _time = time;

        var chosen = settings.Current.Settings.Features.Hardware.EffectiveSensors;
        _sensors = [.. HardwareKeys.Sensors.Where(s => chosen.Contains(s.Key, StringComparer.Ordinal))];
        _entities = BuildEntities();
    }

    /// <inheritdoc />
    public string Id => "hardware";

    /// <summary>The last status published, for tests and diagnostics.</summary>
    public HardwareStatus? Status => _status;

    /// <inheritdoc />
    public bool IsEnabled(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Features.Hardware.Enabled;
    }

    /// <inheritdoc />
    public IReadOnlyList<EntityDescriptor> DescribeEntities() => _entities;

    /// <inheritdoc />
    public Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        Attach(context);
        StartSampling();
        return Task.CompletedTask;
    }

    /// <summary>Starts a session without the sampling loop. Internal for tests that drive <see cref="SampleAsync"/>.</summary>
    internal void Attach(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _startedAt = _time.GetTimestamp();
        _published.Clear();
        _status = null;
        _versionPayload = null;
        _notified = null;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopSamplingAsync().ConfigureAwait(false);
        _context = null;
    }

    /// <inheritdoc />
    public Task OnSuspendingAsync(CancellationToken cancellationToken) => StopSamplingAsync();

    /// <inheritdoc />
    public Task OnResumedAsync(CancellationToken cancellationToken)
    {
        StartSampling();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _sampling?.Cancel();
        _sampling?.Dispose();
    }

    /// <summary>One read of LibreHardwareMonitor. Internal for tests.</summary>
    internal async Task SampleAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.Current.Settings.Features.Hardware;
        var port = settings.Port;

        var trust = LhmTrustEvaluator.Evaluate(_endpoint.Inspect(port), port);
        if (trust.Problem is { } problem)
        {
            await SetStatusAsync(problem, trust.Version, trust.Reason, cancellationToken).ConfigureAwait(false);
            return;
        }

        var version = trust.Version!;
        LhmCredentials? credentials = null;
        if (!string.IsNullOrEmpty(settings.Username))
        {
            try
            {
                var password = string.IsNullOrEmpty(settings.PasswordProtected) ? string.Empty : _protector.Unprotect(settings.PasswordProtected);
                credentials = new LhmCredentials(settings.Username, password);
            }
            catch (CryptographicException)
            {
                await SetStatusAsync(HardwareStatus.LoginFailed, version, "the saved LibreHardwareMonitor password can't be decrypted on this Windows account; enter it again in settings.json", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var response = await _endpoint.GetDataAsync(port, credentials, cancellationToken).ConfigureAwait(false);
        if (response.Outcome != LhmHttpOutcome.Ok || response.Body is null)
        {
            var (status, reason) = Describe(response, port);
            await SetStatusAsync(status, version, reason, cancellationToken).ConfigureAwait(false);
            return;
        }

        var parsed = LhmDataParser.Parse(response.Body);
        if (!parsed.IsValid)
        {
            await SetStatusAsync(HardwareStatus.Incompatible, version, $"its data format changed: {parsed.Problem}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var systemDisk = _sensors.Any(s => s.Key == HardwareKeys.SsdTemperature) ? _endpoint.GetSystemDiskNumber() : null;
        var matches = _sensors.ToDictionary(
            s => s.Key,
            s => LhmSensorMapper.Find(s.Key, parsed.Sensors, settings.SensorOverrides, systemDisk),
            StringComparer.Ordinal);

        // A unit we don't expect means the meaning of the numbers changed: stop rather than publish them.
        foreach (var definition in _sensors)
        {
            if (matches[definition.Key] is { } sensor && !string.Equals(sensor.Unit, definition.LhmUnit, StringComparison.Ordinal))
            {
                await SetStatusAsync(HardwareStatus.Incompatible, version, $"its data format changed: {sensor.SensorId} reads '{sensor.Unit}', expected '{definition.LhmUnit}'", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var support = LhmCompatibility.Classify(version);
        var usable = support == LhmVersionSupport.Newer ? HardwareStatus.NewerThanTested : HardwareStatus.Ok;
        if (_status is not (HardwareStatus.Ok or HardwareStatus.NewerThanTested))
        {
            // Coming back from a problem: republish everything, even values inside the deadband.
            _published.Clear();
        }

        foreach (var definition in _sensors)
        {
            await PublishSensorAsync(definition, matches[definition.Key], cancellationToken).ConfigureAwait(false);
        }

        await SetStatusAsync(usable, version, support == LhmVersionSupport.Newer ? "newer than the tested version" : "ok", cancellationToken).ConfigureAwait(false);
    }

    private static (HardwareStatus Status, string Reason) Describe(LhmHttpResult response, int port) => response.Outcome switch
    {
        LhmHttpOutcome.NotListening => (HardwareStatus.NotRunning, $"nothing answers on port {port}"),
        LhmHttpOutcome.Timeout => (HardwareStatus.NotRunning, $"no answer on port {port} in time"),
        LhmHttpOutcome.LoginRequired => (HardwareStatus.LoginFailed, "LibreHardwareMonitor asks for a login; set features.hardware.username and password"),
        LhmHttpOutcome.LoginFailed => (HardwareStatus.LoginFailed, "LibreHardwareMonitor refused the username or password in settings.json"),
        LhmHttpOutcome.LoginNotRequested => (HardwareStatus.Untrusted, "the server didn't ask for the login set in settings.json; turn on authentication in LibreHardwareMonitor's web server"),
        LhmHttpOutcome.TooLarge => (HardwareStatus.Incompatible, "its data is larger than expected"),
        _ => (HardwareStatus.Incompatible, $"unexpected response ({response.Detail ?? "no detail"})"),
    };

    private List<EntityDescriptor> BuildEntities()
    {
        var available = new EntityAvailability(HardwareKeys.Status, HardwareStatusNames.Usable);
        var entities = _sensors
            .Select(s => new EntityDescriptor(s.Key, s.Name, EntityComponent.Sensor)
            {
                DeviceClass = s.DeviceClass,
                UnitOfMeasurement = s.Unit,
                StateClass = "measurement",
                Icon = s.Icon,
                AvailableWhen = available,
            })
            .ToList();

        entities.Add(new EntityDescriptor(HardwareKeys.Status, "Hardware status", EntityComponent.Sensor)
        {
            DeviceClass = "enum",
            Options = HardwareStatusNames.All,
            Icon = "mdi:chip",
            Category = EntityCategory.Diagnostic,
        });
        entities.Add(new EntityDescriptor(HardwareKeys.LhmVersion, "LibreHardwareMonitor version", EntityComponent.Sensor)
        {
            Icon = "mdi:information-outline",
            Category = EntityCategory.Diagnostic,
        });
        return entities;
    }

    private async Task PublishSensorAsync(HardwareSensorDefinition definition, LhmSensor? sensor, CancellationToken cancellationToken)
    {
        if (_context is not { } context)
        {
            return;
        }

        if (sensor is null)
        {
            if (_warnedUnmatched.Add(definition.Key))
            {
                LogUnmatched(context.Logger, definition.Key);
            }

            await PublishUnknownAsync(context, definition.Key, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (sensor.Value is not { } value)
        {
            await PublishUnknownAsync(context, definition.Key, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (value < definition.Min || value > definition.Max)
        {
            LogImplausible(context.Logger, definition.Key, value);
            return;
        }

        var last = _published.TryGetValue(definition.Key, out var previous) ? previous : null;
        if (HardwareDeadband.ShouldPublish(definition, last, value))
        {
            await context.PublishStateAsync(definition.Key, HardwareDeadband.Format(value), cancellationToken).ConfigureAwait(false);
            _published[definition.Key] = value;
        }
    }

    private async Task PublishUnknownAsync(IModuleContext context, string key, CancellationToken cancellationToken)
    {
        if (_published.TryGetValue(key, out var last) && last is null)
        {
            return; // Already unknown.
        }

        await context.PublishStateAsync(key, UnknownPayload, cancellationToken).ConfigureAwait(false);
        _published[key] = null;
    }

    private async Task SetStatusAsync(HardwareStatus status, Version? version, string reason, CancellationToken cancellationToken)
    {
        if (_context is not { } context)
        {
            return;
        }

        var versionPayload = version is null ? UnknownPayload : LhmCompatibility.Format(version);
        if (versionPayload != _versionPayload)
        {
            await context.PublishStateAsync(HardwareKeys.LhmVersion, versionPayload, cancellationToken).ConfigureAwait(false);
            _versionPayload = versionPayload;
        }

        if (status != _status)
        {
            await context.PublishStateAsync(HardwareKeys.Status, HardwareStatusNames.ToName(status), cancellationToken).ConfigureAwait(false);
            LogStatus(context.Logger, HardwareStatusNames.ToName(status), version is null ? "unknown" : versionPayload, reason);
            _status = status;
        }

        NotifyIfNeeded(status, version, reason);
    }

    private void NotifyIfNeeded(HardwareStatus status, Version? version, string reason)
    {
        if (status == HardwareStatus.Ok)
        {
            _notified = null; // A later problem is news again.
            return;
        }

        if (status == _notified)
        {
            return;
        }

        if (status == HardwareStatus.NotRunning && _time.GetElapsedTime(_startedAt) < NotRunningGrace)
        {
            return; // LHM may still be starting; tell the user only if it stays down.
        }

        _notifier.Notify(NotifyTitle, Message(status, version, reason));
        _notified = status;
    }

    private string Message(HardwareStatus status, Version? version, string reason)
    {
        var found = version is null ? "LibreHardwareMonitor" : $"LibreHardwareMonitor {LhmCompatibility.Format(version)}";
        var tested = LhmCompatibility.Format(LhmCompatibility.TestedVersion);
        return status switch
        {
            HardwareStatus.NewerThanTested => $"{found} is newer than the tested version {tested}. Hardware sensors are on; check that the values look right.",
            HardwareStatus.NotRunning => $"LibreHardwareMonitor isn't running or its web server is off ({reason}). Hardware sensors are paused.",
            HardwareStatus.LoginFailed => $"{Capitalize(reason)}. Hardware sensors are paused.",
            HardwareStatus.Untrusted => $"Hardware sensors are paused: {reason}. See docs/hardware-sensors.md.",
            _ => $"{found} isn't compatible: {reason}. Hardware sensors are paused. Version {tested} is known to work.",
        };
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void StartSampling()
    {
        var sampling = new CancellationTokenSource();
        _sampling = sampling;

        // Called directly so the first delay is registered before this returns (deterministic under a fake clock).
        _samplingLoop = SampleLoopAsync(sampling.Token);
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
        try
        {
            var delay = InitialDelay;
            while (true)
            {
                await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
                try
                {
                    await SampleAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad sample never stops the module (spec: modules are isolated).
                    if (_context?.Logger is { } logger)
                    {
                        LogSampleFailed(logger, ex);
                    }
                }

                delay = TimeSpan.FromSeconds(_settings.Current.Settings.Features.Hardware.IntervalSeconds);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Hardware status {Status} (LibreHardwareMonitor {Version}): {Reason}")]
    private static partial void LogStatus(ILogger logger, string status, string version, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No LibreHardwareMonitor sensor matches {Key}; set features.hardware.sensorOverrides.{Key} to its sensor ID")]
    private static partial void LogUnmatched(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped an implausible {Key} reading of {Value}")]
    private static partial void LogImplausible(ILogger logger, string key, double value);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading LibreHardwareMonitor failed; will retry on the next sample")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
