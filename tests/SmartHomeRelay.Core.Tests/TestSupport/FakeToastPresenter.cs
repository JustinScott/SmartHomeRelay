using System.Collections.Concurrent;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Toasts;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>Records toasts instead of showing them.</summary>
internal sealed class FakeToastPresenter : IToastPresenter
{
    public event EventHandler<string>? Activated;

    public ConcurrentQueue<ToastContent> Shown { get; } = new();

    public ConcurrentQueue<(string Tag, string Group)> Removed { get; } = new();

    public Exception? ThrowOnShow { get; set; }

    public Task ShowAsync(ToastContent content, CancellationToken cancellationToken)
    {
        if (ThrowOnShow is { } ex)
        {
            return Task.FromException(ex);
        }

        Shown.Enqueue(content);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string tag, string group, CancellationToken cancellationToken)
    {
        Removed.Enqueue((tag, group));
        return Task.CompletedTask;
    }

    /// <summary>Simulates a click on a button that doesn't open a URL.</summary>
    public void Click(string arguments) => Activated?.Invoke(this, arguments);
}
