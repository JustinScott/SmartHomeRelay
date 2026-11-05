namespace SmartHomeRelay.Core.Features.Notifications;

/// <summary>
/// A validated notification (spec 5.6). Every URL is an absolute http(s) URL; anything else was dropped while
/// parsing, so nothing here can name a local file.
/// </summary>
public sealed record NotificationRequest
{
    /// <summary>Toast title; may be empty.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Toast message; may be empty (but not together with <see cref="Title"/>).</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Image to download and show, or null.</summary>
    public Uri? HeroImageUrl { get; init; }

    /// <summary>URL opened when the toast is clicked, or null.</summary>
    public Uri? ActivationUrl { get; init; }

    /// <summary>At most 5 URL buttons.</summary>
    public IReadOnlyList<NotificationButton> Buttons { get; init; } = [];
}
