using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>An in-memory audio stack. Setters update the state and raise Changed, like Windows does.</summary>
internal sealed class FakeAudioController : IAudioController
{
    public static readonly AudioDevice Speakers = new("{speakers}", "Speakers (Realtek(R) Audio)");
    public static readonly AudioDevice Headset = new("{headset}", "Headset (USB Audio)");

    public event EventHandler? Changed;

    public AudioState State { get; set; } = new(0.4, false, false, [Speakers, Headset], Speakers.Id);

    public Exception? ThrowOnRead { get; set; }

    public List<string> Calls { get; } = [];

    public int Reads { get; private set; }

    public Task<AudioState> GetStateAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return ThrowOnRead is { } ex ? Task.FromException<AudioState>(ex) : Task.FromResult(State);
    }

    public Task SetVolumeAsync(double level, CancellationToken cancellationToken) =>
        Apply($"volume:{level:0.###}", State with { Volume = level });

    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) =>
        Apply($"muted:{muted}", State with { Muted = muted });

    public Task SetMicMutedAsync(bool muted, CancellationToken cancellationToken) =>
        Apply($"mic:{muted}", State with { MicMuted = muted });

    public Task SetDefaultOutputAsync(string deviceId, CancellationToken cancellationToken) =>
        Apply($"default:{deviceId}", State with { DefaultOutputId = deviceId });

    /// <summary>Simulates a change made outside the app (e.g. headphones plugged in).</summary>
    public void Change(AudioState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Task Apply(string call, AudioState state)
    {
        Calls.Add(call);
        Change(state);
        return Task.CompletedTask;
    }
}
