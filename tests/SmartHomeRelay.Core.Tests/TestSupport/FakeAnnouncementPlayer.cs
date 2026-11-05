using System.Collections.Concurrent;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>Plays until <see cref="Finish"/> is called or the play is cancelled; records everything.</summary>
internal sealed class FakeAnnouncementPlayer : IAnnouncementPlayer
{
    private TaskCompletionSource _playing = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConcurrentQueue<string> Events { get; } = new();

    /// <summary>When true, plays end immediately.</summary>
    public bool Instant { get; set; }

    public Exception? Throw { get; set; }

    public int Started => Events.Count(e => e.StartsWith("play:", StringComparison.Ordinal));

    public async Task PlayAsync(byte[] audio, string contentType, CancellationToken cancellationToken)
    {
        Events.Enqueue($"play:{contentType}:{audio.Length}");
        if (Throw is { } ex)
        {
            throw ex;
        }

        if (Instant)
        {
            return;
        }

        var playing = _playing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await playing.Task.WaitAsync(cancellationToken);
            Events.Enqueue("ended");
        }
        catch (OperationCanceledException)
        {
            Events.Enqueue("stopped");
            throw;
        }
    }

    public void Finish() => _playing.TrySetResult();
}
