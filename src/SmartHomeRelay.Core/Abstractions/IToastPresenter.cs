using SmartHomeRelay.Core.Toasts;

namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Shows Windows toasts (spec 5.6). Standard user; the app registers itself so toasts carry its name.</summary>
public interface IToastPresenter
{
    /// <summary>
    /// Raised when a button that doesn't open a URL is clicked, with that button's arguments. Only while the app
    /// runs; may be raised on any thread.
    /// </summary>
    event EventHandler<string>? Activated;

    /// <summary>Shows a toast. A toast with the same tag and group replaces the earlier one.</summary>
    Task ShowAsync(ToastContent content, CancellationToken cancellationToken);

    /// <summary>Removes the toast with this tag and group from the screen and the notification center.</summary>
    Task RemoveAsync(string tag, string group, CancellationToken cancellationToken);
}
