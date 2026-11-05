using System.Runtime.InteropServices;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

namespace SmartHomeRelay.Platform.Windows.Media;

/// <summary>
/// Ducks other apps through their Core Audio sessions on the default output (spec 5.9): each session's own
/// volume (the one in Windows' volume mixer) is faded to a fraction of itself and back. Our own process and
/// the system sounds session are left alone. Apps in exclusive mode have no shared session and can't be ducked.
/// Standard user; COM on thread-pool (MTA) threads.
/// </summary>
public sealed class WindowsAudioDucker : IAudioDucker
{
    /// <summary>How long a fade takes.</summary>
    public static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(300);

    private const int FadeSteps = 10;
    private const int SessionStateExpired = 2;
    private static readonly Guid s_eventContext = Guid.NewGuid();

    private readonly DuckedVolumesFile _file;
    private readonly Lock _gate = new();
    private List<(DuckedSession Saved, ISimpleAudioVolume Volume)>? _ducked;

    /// <summary>Creates the ducker; the saved levels live next to the logs folder.</summary>
    public WindowsAudioDucker(IAppPaths paths)
        : this(Path.Combine(Path.GetDirectoryName(paths?.LogsDirectory ?? throw new ArgumentNullException(nameof(paths)))!, "ducked-volumes.json"))
    {
    }

    internal WindowsAudioDucker(string filePath)
    {
        _file = new DuckedVolumesFile(filePath);
    }

    /// <inheritdoc />
    public Task DuckAsync(double fraction, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                lock (_gate)
                {
                    if (_ducked is not null)
                    {
                        return;
                    }

                    var ducked = new List<(DuckedSession, ISimpleAudioVolume)>();
                    foreach (var (sessionId, instanceId, volume) in OtherSessions())
                    {
                        var original = volume.GetMasterVolume();
                        ducked.Add((new DuckedSession(sessionId, instanceId, original, (float)(original * Math.Clamp(fraction, 0, 1))), volume));
                    }

                    // Saved before anything changes, so a crash mid-fade can still be undone.
                    _file.Save([.. ducked.Select(d => d.Item1)]);
                    _ducked = ducked;
                    Fade(ducked.Select(d => (d.Item2, d.Item1.Original, d.Item1.Ducked)).ToList());
                }
            },
            cancellationToken);

    /// <inheritdoc />
    public Task RestoreAsync(CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                lock (_gate)
                {
                    if (_ducked is not { } ducked)
                    {
                        return;
                    }

                    _ducked = null;
                    var fades = new List<(ISimpleAudioVolume, float, float)>();
                    foreach (var (saved, volume) in ducked)
                    {
                        if (TryGet(volume) is { } current && saved.IsUntouched(current))
                        {
                            fades.Add((volume, current, saved.Original));
                        }
                    }

                    Fade(fades);
                    _file.Delete();
                }
            },
            CancellationToken.None);

    /// <inheritdoc />
    public Task RecoverAsync(CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                lock (_gate)
                {
                    if (_ducked is not null)
                    {
                        return; // A duck of ours is in progress; its restore handles it.
                    }

                    var saved = _file.Load();
                    if (saved.Count == 0)
                    {
                        _file.Delete();
                        return;
                    }

                    foreach (var (sessionId, instanceId, volume) in OtherSessions())
                    {
                        var match = saved.FirstOrDefault(s => s.InstanceId == instanceId) ?? saved.FirstOrDefault(s => s.SessionId == sessionId);
                        if (match is not null && TryGet(volume) is { } current && match.IsUntouched(current))
                        {
                            Set(volume, match.Original);
                        }
                    }

                    _file.Delete();
                }
            },
            cancellationToken);

    private static float? TryGet(ISimpleAudioVolume volume)
    {
        try
        {
            return volume.GetMasterVolume();
        }
        catch (COMException)
        {
            return null; // The app closed.
        }
    }

    private static void Set(ISimpleAudioVolume volume, float level)
    {
        try
        {
            var context = s_eventContext;
            volume.SetMasterVolume(Math.Clamp(level, 0f, 1f), ref context);
        }
        catch (COMException)
        {
            // The app closed mid-fade.
        }
    }

    private static void Fade(List<(ISimpleAudioVolume Volume, float From, float To)> fades)
    {
        if (fades.Count == 0)
        {
            return;
        }

        var pause = FadeDuration / FadeSteps;
        for (var step = 1; step <= FadeSteps; step++)
        {
            foreach (var (volume, from, to) in fades)
            {
                Set(volume, from + ((to - from) * step / FadeSteps));
            }

            if (step < FadeSteps)
            {
                Thread.Sleep(pause);
            }
        }
    }

    /// <summary>Every live app session on the default output except ours and the system sounds.</summary>
    private static List<(string SessionId, string InstanceId, ISimpleAudioVolume Volume)> OtherSessions()
    {
        var result = new List<(string, string, ISimpleAudioVolume)>();
        var enumerator = CoreAudioMethods.Create<IMMDeviceEnumerator>(CoreAudioMethods.DeviceEnumeratorClassId);
        if (CoreAudioMethods.TryGetDefault(enumerator, DataFlow.Render) is not { } device)
        {
            return result;
        }

        var sessions = CoreAudioMethods.ActivateSessions(device).GetSessionEnumerator();
        var ownProcess = (uint)Environment.ProcessId;
        for (var i = 0; i < sessions.GetCount(); i++)
        {
            try
            {
                var session = sessions.GetSession(i);
                if (session is not IAudioSessionControl2 control
                    || control.GetState() == SessionStateExpired
                    || control.IsSystemSoundsSession() == 0
                    || control.GetProcessId() == ownProcess
                    || session is not ISimpleAudioVolume volume)
                {
                    continue;
                }

                result.Add((control.GetSessionIdentifier(), control.GetSessionInstanceIdentifier(), volume));
            }
            catch (COMException)
            {
                // A session that went away while listing.
            }
        }

        return result;
    }
}
