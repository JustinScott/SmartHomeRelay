using System.Globalization;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Features.Audio;

/// <summary>
/// Audio (spec 5.4): volume (slider), mute, microphone mute, and the default output device (a select whose
/// options follow the devices plugged in).
/// <list type="bullet">
/// <item>Windows' change notifications trigger a refresh (coalesced), plus a periodic safety refresh.</item>
/// <item>Only values that changed are published.</item>
/// <item>Commands go to Windows; the resulting notification publishes the new state.</item>
/// </list>
/// </summary>
public sealed partial class AudioModule : IFeatureModule, IPowerAware, IDisposable
{
    /// <summary>How often the state is re-read even without a notification.</summary>
    public static readonly TimeSpan SafetyRefreshInterval = TimeSpan.FromSeconds(60);

    /// <summary>How long notifications are gathered before one refresh (a volume drag sends many).</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    /// <summary>The MQTT payload HA reads as "unknown".</summary>
    public const string UnknownPayload = "None";

    private readonly IAudioController _audio;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dictionary<string, string> _published = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<EntityDescriptor> _entities;
    private IReadOnlyList<(string Option, string Id)> _devices = [];
    private IModuleContext? _context;
    private CancellationTokenSource? _running;
    private Task? _safetyLoop;
    private int _refreshQueued;
    private int _forceOutputRepublish;
    private int _refreshWorker;

    /// <summary>Creates the module.</summary>
    public AudioModule(IAudioController audio, TimeProvider time)
    {
        _audio = audio;
        _time = time;
        _entities =
        [
            new(AudioKeys.Volume, "Volume", EntityComponent.Number)
            {
                Range = new NumberRange(0, 100, 1),
                Mode = NumberMode.Slider,
                UnitOfMeasurement = "%",
                Icon = "mdi:volume-high",
                CommandHandler = SetVolumeAsync,
            },
            new(AudioKeys.Muted, "Muted", EntityComponent.Switch)
            {
                Icon = "mdi:volume-off",
                CommandHandler = (payload, ct) => SetSwitchAsync(AudioKeys.Muted, payload, _audio.SetMutedAsync, ct),
            },
            new(AudioKeys.MicMuted, "Microphone muted", EntityComponent.Switch)
            {
                Icon = "mdi:microphone-off",
                CommandHandler = (payload, ct) => SetSwitchAsync(AudioKeys.MicMuted, payload, _audio.SetMicMutedAsync, ct),
            },
            OutputDevice([AudioDeviceNames.NoDevice]),
        ];
    }

    /// <inheritdoc />
    public string Id => "audio";

    /// <inheritdoc />
    public bool IsEnabled(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Features.Audio.Enabled;
    }

    /// <inheritdoc />
    public IReadOnlyList<EntityDescriptor> DescribeEntities() => _entities;

    /// <inheritdoc />
    public async Task StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _published.Clear();
        _devices = [];

        // Read once before returning, so the select's real options are in place before the first discovery.
        await RefreshAsync(cancellationToken).ConfigureAwait(false);

        var running = new CancellationTokenSource();
        _running = running;
        _audio.Changed += OnAudioChanged;
        _safetyLoop = SafetyLoopAsync(running.Token);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _audio.Changed -= OnAudioChanged;
        var running = _running;
        var loop = _safetyLoop;
        _running = null;
        _safetyLoop = null;
        if (running is not null)
        {
            await running.CancelAsync().ConfigureAwait(false);
            if (loop is not null)
            {
                await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            running.Dispose();
        }

        _context = null;
    }

    /// <inheritdoc />
    public Task OnSuspendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnResumedAsync(CancellationToken cancellationToken)
    {
        // Devices can come and go while asleep (e.g. a dock).
        RequestRefresh();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _audio.Changed -= OnAudioChanged;
        _running?.Cancel();
        _running?.Dispose();
        _refreshGate.Dispose();
    }

    /// <summary>Reads Windows' audio state and publishes what changed. Internal for tests.</summary>
    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_context is not { } context)
        {
            return;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AudioState state;
            try
            {
                state = await _audio.GetStateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogReadFailed(context.Logger, ex);
                return;
            }

            var devices = AudioDeviceNames.Build(state.OutputDevices);
            if (!devices.Select(d => d.Option).SequenceEqual(_devices.Select(d => d.Option), StringComparer.Ordinal))
            {
                IReadOnlyList<string> options = devices.Count == 0 ? [AudioDeviceNames.NoDevice] : [.. devices.Select(d => d.Option)];
                await context.UpdateEntityAsync(OutputDevice(options), cancellationToken).ConfigureAwait(false);

                // HA re-reads the state after a config change; make sure it's sent again.
                _published.Remove(AudioKeys.OutputDevice);
            }

            Volatile.Write(ref _devices, devices);
            if (Interlocked.Exchange(ref _forceOutputRepublish, 0) == 1)
            {
                _published.Remove(AudioKeys.OutputDevice);
            }

            var defaultOption = devices.Count == 0
                ? AudioDeviceNames.NoDevice
                : devices.FirstOrDefault(d => d.Id == state.DefaultOutputId).Option ?? UnknownPayload;

            await PublishIfChangedAsync(context, AudioKeys.Volume, state.Volume is { } v ? Percent(v) : UnknownPayload, cancellationToken).ConfigureAwait(false);
            await PublishIfChangedAsync(context, AudioKeys.Muted, OnOff(state.Muted), cancellationToken).ConfigureAwait(false);
            await PublishIfChangedAsync(context, AudioKeys.MicMuted, OnOff(state.MicMuted), cancellationToken).ConfigureAwait(false);
            await PublishIfChangedAsync(context, AudioKeys.OutputDevice, defaultOption, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>Volume as a whole percentage, e.g. <c>40</c>.</summary>
    internal static string Percent(double level) =>
        Math.Round(Math.Clamp(level, 0, 1) * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);

    private static string OnOff(bool? value) => value switch
    {
        true => DiscoveryPayloadBuilder.On,
        false => DiscoveryPayloadBuilder.Off,
        null => UnknownPayload,
    };

    private EntityDescriptor OutputDevice(IReadOnlyList<string> options) =>
        new(AudioKeys.OutputDevice, "Output device", EntityComponent.Select)
        {
            Options = options,
            Icon = "mdi:speaker",
            CommandHandler = SetOutputDeviceAsync,
        };

    private async Task PublishIfChangedAsync(IModuleContext context, string key, string payload, CancellationToken cancellationToken)
    {
        if (_published.TryGetValue(key, out var last) && last == payload)
        {
            return;
        }

        await context.PublishStateAsync(key, payload, cancellationToken).ConfigureAwait(false);
        _published[key] = payload;
    }

    private async Task SetVolumeAsync(string payload, CancellationToken cancellationToken)
    {
        if (!double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) || !double.IsFinite(percent))
        {
            Log(l => LogBadPayload(l, AudioKeys.Volume));
            return;
        }

        await _audio.SetVolumeAsync(Math.Clamp(percent, 0, 100) / 100, cancellationToken).ConfigureAwait(false);
        RequestRefresh();
    }

    private async Task SetSwitchAsync(string key, string payload, Func<bool, CancellationToken, Task> set, CancellationToken cancellationToken)
    {
        bool value;
        if (string.Equals(payload, DiscoveryPayloadBuilder.On, StringComparison.OrdinalIgnoreCase))
        {
            value = true;
        }
        else if (string.Equals(payload, DiscoveryPayloadBuilder.Off, StringComparison.OrdinalIgnoreCase))
        {
            value = false;
        }
        else
        {
            Log(l => LogBadPayload(l, key));
            return;
        }

        await set(value, cancellationToken).ConfigureAwait(false);
        RequestRefresh();
    }

    private async Task SetOutputDeviceAsync(string payload, CancellationToken cancellationToken)
    {
        var device = Volatile.Read(ref _devices).FirstOrDefault(d => string.Equals(d.Option, payload, StringComparison.Ordinal));
        if (device.Id is null)
        {
            Log(l => LogUnknownDevice(l, payload));

            // Put HA's select back to the real default.
            Interlocked.Exchange(ref _forceOutputRepublish, 1);
            RequestRefresh();
            return;
        }

        await _audio.SetDefaultOutputAsync(device.Id, cancellationToken).ConfigureAwait(false);
        RequestRefresh();
    }

    private void OnAudioChanged(object? sender, EventArgs e) => RequestRefresh();

    /// <summary>Coalesces notifications: one worker drains requests, waiting <see cref="Debounce"/> each time.</summary>
    private void RequestRefresh()
    {
        Interlocked.Exchange(ref _refreshQueued, 1);
        if (Interlocked.CompareExchange(ref _refreshWorker, 1, 0) == 0)
        {
            _ = Task.Run(DrainRefreshesAsync, CancellationToken.None);
        }
    }

    private async Task DrainRefreshesAsync()
    {
        while (true)
        {
            try
            {
                while (Interlocked.Exchange(ref _refreshQueued, 0) == 1)
                {
                    var token = _running?.Token ?? CancellationToken.None;
                    await Task.Delay(Debounce, _time, token).ConfigureAwait(false);
                    await RefreshAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log(l => LogReadFailed(l, ex));
            }

            Interlocked.Exchange(ref _refreshWorker, 0);

            // A request that arrived after the last check but before the worker flag cleared still gets served.
            if (Volatile.Read(ref _refreshQueued) == 0 || Interlocked.CompareExchange(ref _refreshWorker, 1, 0) != 0)
            {
                return;
            }
        }
    }

    private async Task SafetyLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(SafetyRefreshInterval, _time, cancellationToken).ConfigureAwait(false);
                RequestRefresh();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Log(Action<ILogger> write)
    {
        if (_context?.Logger is { } logger)
        {
            write(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the audio state; will retry")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored {EntityKey}: unexpected payload")]
    private static partial void LogBadPayload(ILogger logger, string entityKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored output device '{Option}': no such device now")]
    private static partial void LogUnknownDevice(ILogger logger, string option);
}
