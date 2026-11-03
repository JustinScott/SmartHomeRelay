using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;

namespace SmartHomeRelay.Core.Features.Metrics;

/// <summary>
/// System metrics (spec 5.5): CPU, memory, per-drive disk, network rates, and GPU.
/// <list type="bullet">
/// <item>Sampled every <c>features.metrics.intervalSeconds</c> (default 30), read fresh each time.</item>
/// <item>Published only on a meaningful change (<see cref="MetricDeadband"/>).</item>
/// <item>Drive entities are the fixed drives present at startup; GPU only if its counters exist.</item>
/// <item>Sampling pauses during sleep; rate baselines are reset on wake.</item>
/// </list>
/// </summary>
public sealed partial class MetricsModule : IFeatureModule, IPowerAware, IDisposable
{
    /// <summary>Delay before the first full sample, so CPU and network have a baseline to compare with.</summary>
    public static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(2);

    private readonly ISystemMetricsSource _source;
    private readonly ISettingsState _settings;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<char> _driveLetters;
    private readonly bool _gpu;
    private readonly IReadOnlyList<EntityDescriptor> _entities;
    private readonly Dictionary<string, double> _published = new(StringComparer.Ordinal);
    private IModuleContext? _context;
    private CancellationTokenSource? _sampling;
    private Task? _samplingLoop;
    private CpuTimes? _lastCpu;
    private IReadOnlyList<NetworkAdapterCounters>? _lastNetwork;
    private long _lastNetworkTimestamp;

    /// <summary>Creates the module. Which drives and whether GPU are offered is decided here, once.</summary>
    public MetricsModule(ISystemMetricsSource source, ISettingsState settings, TimeProvider time)
    {
        _source = source;
        _settings = settings;
        _time = time;

        // Decided once so DescribeEntities returns the same keys every time (module contract).
        _driveLetters = TryOrDefault(() => source.GetFixedDrives().Select(d => char.ToUpperInvariant(d.Letter)).Distinct().ToList(), []);
        _gpu = TryOrDefault(() => source.IsGpuAvailable, false);
        _entities = BuildEntities();
    }

    /// <inheritdoc />
    public string Id => "metrics";

    /// <inheritdoc />
    public bool IsEnabled(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Features.Metrics.Enabled;
    }

    /// <inheritdoc />
    public IReadOnlyList<EntityDescriptor> DescribeEntities() => _entities;

    /// <inheritdoc />
    public async Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _published.Clear();
        ResetBaselines();

        // Memory and disk don't need a baseline: publish them right away.
        await SampleAsync(includeRates: false, cancellationToken).ConfigureAwait(false);
        StartSampling();
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
        // Counters across sleep don't make sense as a rate (and adapters may have reset): start fresh.
        ResetBaselines();
        StartSampling();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _sampling?.Cancel();
        _sampling?.Dispose();
    }

    private List<EntityDescriptor> BuildEntities()
    {
        var entities = new List<EntityDescriptor>
        {
            Percent(MetricsKeys.CpuUsage, "CPU usage", "mdi:cpu-64-bit"),
            Percent(MetricsKeys.MemoryUsage, "Memory usage", "mdi:memory"),
            Gigabytes(MetricsKeys.MemoryUsedGb, "Memory used", "mdi:memory"),
        };

        foreach (var letter in _driveLetters)
        {
            entities.Add(Percent(MetricsKeys.DiskUsage(letter), $"Disk {letter}: usage", "mdi:harddisk"));
            entities.Add(Gigabytes(MetricsKeys.DiskFree(letter), $"Disk {letter}: free", "mdi:harddisk"));
        }

        entities.Add(Rate(MetricsKeys.NetworkDown, "Network download", "mdi:download-network"));
        entities.Add(Rate(MetricsKeys.NetworkUp, "Network upload", "mdi:upload-network"));

        if (_gpu)
        {
            entities.Add(Percent(MetricsKeys.GpuUsage, "GPU usage", "mdi:expansion-card"));
        }

        return entities;
    }

    private static EntityDescriptor Percent(string key, string name, string icon) =>
        new(key, name, EntityComponent.Sensor) { UnitOfMeasurement = "%", StateClass = "measurement", Icon = icon };

    private static EntityDescriptor Gigabytes(string key, string name, string icon) =>
        new(key, name, EntityComponent.Sensor)
        {
            DeviceClass = "data_size",

            // Values use 1024^3 bytes, like Task Manager and Explorer. HA reads "GB" as 10^9, so label it GiB.
            UnitOfMeasurement = "GiB",
            StateClass = "measurement",
            Icon = icon,
        };

    private static EntityDescriptor Rate(string key, string name, string icon) =>
        new(key, name, EntityComponent.Sensor)
        {
            DeviceClass = "data_rate",
            UnitOfMeasurement = "Mbit/s",
            StateClass = "measurement",
            Icon = icon,
        };

    private void ResetBaselines()
    {
        _lastCpu = TryOrDefault<CpuTimes?>(() => _source.GetCpuTimes(), null);
        _lastNetwork = TryOrDefault<IReadOnlyList<NetworkAdapterCounters>?>(() => _source.GetNetworkAdapters(), null);
        _lastNetworkTimestamp = _time.GetTimestamp();
        if (_gpu)
        {
            TryOrDefault(() => _source.GetGpuEngineUtilization(), null); // primes the counters
        }
    }

    private void StartSampling()
    {
        var sampling = new CancellationTokenSource();
        _sampling = sampling;

        // Called directly (not Task.Run) so the first delay is registered before this returns: the
        // schedule then starts exactly at StartAsync, which also makes it deterministic under a fake clock.
        // The loop continues on the thread pool after its first await.
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
            var delay = WarmUp;
            while (true)
            {
                // A delay re-read every time (not a fixed timer), so an interval change applies on the next
                // sample, and a gap (e.g. Modern Standby throttling) just makes one sample late.
                await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
                await SampleAsync(includeRates: true, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(_settings.Current.Settings.Features.Metrics.IntervalSeconds);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Reads every metric; each is isolated so one failing counter doesn't stop the others.</summary>
    private async Task SampleAsync(bool includeRates, CancellationToken cancellationToken)
    {
        if (includeRates)
        {
            await MeasureAsync("CPU", MetricsKeys.CpuUsage, MetricKind.Percent, ReadCpu, cancellationToken).ConfigureAwait(false);
        }

        MemoryStatus? memory = TryOrDefault<MemoryStatus?>(() => _source.GetMemory(), null);
        await MeasureAsync("memory", MetricsKeys.MemoryUsage, MetricKind.Percent, () => memory is { } m ? MetricsCalculator.MemoryUsagePercent(m) : null, cancellationToken).ConfigureAwait(false);
        await MeasureAsync("memory", MetricsKeys.MemoryUsedGb, MetricKind.Gigabytes, () => memory is { } m ? MetricsCalculator.MemoryUsedGigabytes(m) : null, cancellationToken).ConfigureAwait(false);

        if (_driveLetters.Count > 0)
        {
            var drives = TryOrDefault<IReadOnlyList<DriveSpace>>(() => _source.GetFixedDrives(), []);
            foreach (var letter in _driveLetters)
            {
                var drive = drives.FirstOrDefault(d => char.ToUpperInvariant(d.Letter) == letter);
                await MeasureAsync("disk", MetricsKeys.DiskUsage(letter), MetricKind.Percent, () => drive is null ? null : MetricsCalculator.DiskUsagePercent(drive), cancellationToken).ConfigureAwait(false);
                await MeasureAsync("disk", MetricsKeys.DiskFree(letter), MetricKind.Gigabytes, () => drive is null ? null : MetricsCalculator.DiskFreeGigabytes(drive), cancellationToken).ConfigureAwait(false);
            }
        }

        if (includeRates)
        {
            var rates = ReadNetwork();
            await MeasureAsync("network", MetricsKeys.NetworkDown, MetricKind.Rate, () => rates?.DownMbps, cancellationToken).ConfigureAwait(false);
            await MeasureAsync("network", MetricsKeys.NetworkUp, MetricKind.Rate, () => rates?.UpMbps, cancellationToken).ConfigureAwait(false);

            if (_gpu)
            {
                await MeasureAsync("GPU", MetricsKeys.GpuUsage, MetricKind.Percent, () => MetricsCalculator.GpuUsagePercent(_source.GetGpuEngineUtilization()), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private double? ReadCpu()
    {
        var current = _source.GetCpuTimes();
        var previous = _lastCpu;
        _lastCpu = current;
        return previous is { } p ? MetricsCalculator.CpuUsagePercent(p, current) : null;
    }

    private (double DownMbps, double UpMbps)? ReadNetwork()
    {
        try
        {
            var current = _source.GetNetworkAdapters();
            var now = _time.GetTimestamp();
            var previous = _lastNetwork;
            var elapsed = _time.GetElapsedTime(_lastNetworkTimestamp, now);
            _lastNetwork = current;
            _lastNetworkTimestamp = now;
            return previous is null ? null : MetricsCalculator.NetworkRates(previous, current, elapsed);
        }
        catch (Exception ex)
        {
            LogMetricFailed(ex, "network");
            return null;
        }
    }

    private async Task MeasureAsync(string metric, string key, MetricKind kind, Func<double?> read, CancellationToken cancellationToken)
    {
        if (_context is not { } context)
        {
            return;
        }

        double? value;
        try
        {
            value = read();
        }
        catch (Exception ex)
        {
            LogMetricFailed(ex, metric);
            return;
        }

        if (value is not { } current || double.IsNaN(current)
            || !MetricDeadband.ShouldPublish(kind, _published.TryGetValue(key, out var last) ? last : null, current))
        {
            return;
        }

        await context.PublishStateAsync(key, MetricDeadband.Format(kind, current), cancellationToken).ConfigureAwait(false);
        _published[key] = current;
    }

    private void LogMetricFailed(Exception exception, string metric)
    {
        if (_context?.Logger is { } logger)
        {
            LogReadFailed(logger, exception, metric);
        }
    }

    private static T TryOrDefault<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the {Metric} metric; will retry on the next sample")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, string metric);
}
