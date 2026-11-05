using System.Runtime.InteropServices;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

namespace SmartHomeRelay.Platform.Windows.Audio;

/// <summary>
/// The PC's audio through Windows Core Audio, with our own COM interop (no package; spec 5.4). Every COM call
/// runs on a thread-pool (MTA) thread, so nothing is marshaled to the tray's UI thread. Windows' device and
/// volume notifications raise <see cref="Changed"/>; the module re-reads the state.
/// </summary>
public sealed class WindowsAudioController : IAudioController, IDisposable
{
    private static readonly Guid s_eventContext = Guid.NewGuid();

    private readonly Lock _gate = new();
    private readonly NotificationClient _notifications;
    private readonly VolumeCallback _volumeCallback;
    private IMMDeviceEnumerator? _enumerator;
    private (string Id, IAudioEndpointVolume Volume)? _renderWatch;
    private (string Id, IAudioEndpointVolume Volume)? _captureWatch;
    private bool _disposed;

    /// <summary>Creates the controller. COM objects are created on first use.</summary>
    public WindowsAudioController()
    {
        _notifications = new NotificationClient(RaiseChanged);
        _volumeCallback = new VolumeCallback(RaiseChanged);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public Task<AudioState> GetStateAsync(CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                lock (_gate)
                {
                    var enumerator = Enumerator();
                    var devices = new List<AudioDevice>();
                    var collection = enumerator.EnumAudioEndpoints(DataFlow.Render, CoreAudioMethods.DeviceStateActive);
                    for (var i = 0; i < collection.GetCount(); i++)
                    {
                        var device = collection.Item(i);
                        devices.Add(new AudioDevice(device.GetId(), CoreAudioMethods.FriendlyName(device)));
                    }

                    var render = CoreAudioMethods.TryGetDefault(enumerator, DataFlow.Render);
                    var capture = CoreAudioMethods.TryGetDefault(enumerator, DataFlow.Capture);
                    var renderVolume = Watch(ref _renderWatch, render);
                    var captureVolume = Watch(ref _captureWatch, capture);

                    return new AudioState(
                        renderVolume?.GetMasterVolumeLevelScalar(),
                        renderVolume?.GetMute(),
                        captureVolume?.GetMute(),
                        devices,
                        render?.GetId());
                }
            },
            cancellationToken);

    /// <inheritdoc />
    public Task SetVolumeAsync(double level, CancellationToken cancellationToken) =>
        OnDefault(DataFlow.Render, v =>
        {
            var context = s_eventContext;
            v.SetMasterVolumeLevelScalar((float)Math.Clamp(level, 0, 1), ref context);
        }, cancellationToken);

    /// <inheritdoc />
    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) =>
        OnDefault(DataFlow.Render, v =>
        {
            var context = s_eventContext;
            v.SetMute(muted, ref context);
        }, cancellationToken);

    /// <inheritdoc />
    public Task SetMicMutedAsync(bool muted, CancellationToken cancellationToken) =>
        OnDefault(DataFlow.Capture, v =>
        {
            var context = s_eventContext;
            v.SetMute(muted, ref context);
        }, cancellationToken);

    /// <inheritdoc />
    public Task SetDefaultOutputAsync(string deviceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        return Task.Run(
            () =>
            {
                lock (_gate)
                {
                    // Only switch to a device that exists and is active right now.
                    var device = Enumerator().GetDevice(deviceId);
                    if (device.GetState() != CoreAudioMethods.DeviceStateActive)
                    {
                        throw new InvalidOperationException("That audio device isn't active.");
                    }

                    var policy = CoreAudioMethods.Create<IPolicyConfig>(CoreAudioMethods.PolicyConfigClassId);
                    foreach (var role in new[] { DeviceRole.Console, DeviceRole.Multimedia, DeviceRole.Communications })
                    {
                        Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
                    }
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Unwatch(ref _renderWatch);
            Unwatch(ref _captureWatch);
            if (_enumerator is { } enumerator)
            {
                try
                {
                    enumerator.UnregisterEndpointNotificationCallback(_notifications);
                }
                catch (COMException)
                {
                }
            }
        }
    }

    private Task OnDefault(DataFlow flow, Action<IAudioEndpointVolume> action, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                lock (_gate)
                {
                    var device = CoreAudioMethods.TryGetDefault(Enumerator(), flow)
                        ?? throw new InvalidOperationException(flow == DataFlow.Render ? "There is no playback device." : "There is no microphone.");
                    action(CoreAudioMethods.ActivateVolume(device));
                }
            },
            cancellationToken);

    private IMMDeviceEnumerator Enumerator()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_enumerator is null)
        {
            _enumerator = CoreAudioMethods.Create<IMMDeviceEnumerator>(CoreAudioMethods.DeviceEnumeratorClassId);
            try
            {
                _enumerator.RegisterEndpointNotificationCallback(_notifications);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                // Without notifications the module's periodic refresh still keeps the state current.
            }
        }

        return _enumerator;
    }

    /// <summary>Keeps a volume notification on the current default device, moving it when the default changes.</summary>
    private IAudioEndpointVolume? Watch(ref (string Id, IAudioEndpointVolume Volume)? watch, IMMDevice? device)
    {
        if (device is null)
        {
            Unwatch(ref watch);
            return null;
        }

        var id = device.GetId();
        if (watch is { } current && current.Id == id)
        {
            return current.Volume;
        }

        Unwatch(ref watch);
        var volume = CoreAudioMethods.ActivateVolume(device);
        try
        {
            volume.RegisterControlChangeNotify(_volumeCallback);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // As above: the periodic refresh covers it.
        }

        watch = (id, volume);
        return volume;
    }

    private void Unwatch(ref (string Id, IAudioEndpointVolume Volume)? watch)
    {
        if (watch is { } current)
        {
            try
            {
                current.Volume.UnregisterControlChangeNotify(_volumeCallback);
            }
            catch (COMException)
            {
                // The device is gone; nothing to unregister from.
            }
        }

        watch = null;
    }

    /// <summary>Never raised inline: Windows' notification threads must not be blocked or re-entered.</summary>
    private void RaiseChanged() =>
        ThreadPool.QueueUserWorkItem(static self => self.Changed?.Invoke(self, EventArgs.Empty), this, preferLocal: false);

    [ComVisible(true)]
    private sealed class NotificationClient(Action changed) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, int newState) => Raise();

        public int OnDeviceAdded(string deviceId) => Raise();

        public int OnDeviceRemoved(string deviceId) => Raise();

        public int OnDefaultDeviceChanged(DataFlow flow, DeviceRole role, string? defaultDeviceId) =>
            role == DeviceRole.Console ? Raise() : 0;

        public int OnPropertyValueChanged(string deviceId, PropertyKey key) =>
            key.FormatId == PropertyKey.DeviceFriendlyName.FormatId && key.PropertyId == PropertyKey.DeviceFriendlyName.PropertyId ? Raise() : 0;

        private int Raise()
        {
            changed();
            return 0;
        }
    }

    [ComVisible(true)]
    private sealed class VolumeCallback(Action changed) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint notifyData)
        {
            changed();
            return 0;
        }
    }
}
