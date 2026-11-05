using System.Collections.Concurrent;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>An in-memory Windows media session.</summary>
internal sealed class FakeMediaSession : IMediaSessionSource
{
    public event EventHandler? Changed;

    public MediaSessionInfo? Current { get; set; } = new(MediaPlaybackStatus.Playing, "Song", "Artist", "Album", TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(10), "music");

    public ConcurrentQueue<string> Calls { get; } = new();

    public bool Refuse { get; set; }

    public Task<MediaSessionInfo?> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

    public Task<bool> SendAsync(MediaSessionCommand command, CancellationToken cancellationToken)
    {
        Calls.Enqueue(command.ToString());
        if (!Refuse && Current is { } current)
        {
            Current = command switch
            {
                MediaSessionCommand.Pause => current with { Status = MediaPlaybackStatus.Paused },
                MediaSessionCommand.Play => current with { Status = MediaPlaybackStatus.Playing },
                _ => current,
            };
        }

        return Task.FromResult(!Refuse && Current is not null);
    }

    public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        Calls.Enqueue($"Seek:{position.TotalSeconds:0.##}");
        return Task.FromResult(!Refuse);
    }

    public void Change(MediaSessionInfo? info)
    {
        Current = info;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
