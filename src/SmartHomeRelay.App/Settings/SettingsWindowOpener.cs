using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.App.Settings;

/// <summary>
/// <see cref="ISettingsWindow"/> for the tray. A request made before the tray exists (the first-run welcome,
/// during host start) is kept and served when the tray attaches.
/// </summary>
internal sealed class SettingsWindowOpener : ISettingsWindow
{
    private readonly Lock _gate = new();
    private Action? _open;
    private bool _pending;

    public void Open()
    {
        Action? open;
        lock (_gate)
        {
            open = _open;
            if (open is null)
            {
                _pending = true;
                return;
            }
        }

        open();
    }

    /// <summary>Called by the tray. <paramref name="open"/> must be thread-safe (it posts to the UI thread).</summary>
    public void Attach(Action open)
    {
        bool pending;
        lock (_gate)
        {
            _open = open;
            pending = _pending;
            _pending = false;
        }

        if (pending)
        {
            open();
        }
    }

    public void Detach()
    {
        lock (_gate)
        {
            _open = null;
        }
    }
}
