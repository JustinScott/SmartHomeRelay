using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.App.Tray;

/// <summary>
/// <see cref="IUserNotifier"/> backed by tray balloons. Messages sent before the tray exists (e.g. the
/// first-run welcome, sent during host start) are queued and shown when the tray attaches.
/// </summary>
internal sealed class TrayNotifier : IUserNotifier
{
    private readonly Lock _gate = new();
    private readonly Queue<(string Title, string Message)> _pending = new();
    private Action<string, string>? _show;

    public void Notify(string title, string message)
    {
        Action<string, string>? show;
        lock (_gate)
        {
            show = _show;
            if (show is null)
            {
                _pending.Enqueue((title, message));
                return;
            }
        }

        show(title, message);
    }

    /// <summary>Called by the tray once it can show balloons. <paramref name="show"/> must be thread-safe.</summary>
    public void Attach(Action<string, string> show)
    {
        List<(string Title, string Message)> pending;
        lock (_gate)
        {
            _show = show;
            pending = [.. _pending];
            _pending.Clear();
        }

        foreach (var (title, message) in pending)
        {
            show(title, message);
        }
    }

    public void Detach()
    {
        lock (_gate)
        {
            _show = null;
        }
    }
}
