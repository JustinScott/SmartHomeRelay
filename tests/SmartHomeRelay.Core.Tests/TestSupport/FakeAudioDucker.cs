using System.Collections.Concurrent;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

internal sealed class FakeAudioDucker : IAudioDucker
{
    public ConcurrentQueue<string> Calls { get; } = new();

    public Task DuckAsync(double fraction, CancellationToken cancellationToken)
    {
        Calls.Enqueue($"duck:{fraction:0.##}");
        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken cancellationToken)
    {
        Calls.Enqueue("restore");
        return Task.CompletedTask;
    }

    public Task RecoverAsync(CancellationToken cancellationToken)
    {
        Calls.Enqueue("recover");
        return Task.CompletedTask;
    }
}
