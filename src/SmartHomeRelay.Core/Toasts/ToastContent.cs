namespace SmartHomeRelay.Core.Toasts;

/// <summary>What a Windows toast shows, independent of the Windows API (spec 5.6).</summary>
public sealed record ToastContent
{
    /// <summary>First line, in bold. May be empty.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Body text. May be empty.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>A local image file shown across the top, or null.</summary>
    public string? HeroImagePath { get; init; }

    /// <summary>An http(s) URL Windows opens when the toast is clicked, or null.</summary>
    public Uri? LaunchUrl { get; init; }

    /// <summary>Up to 5 buttons.</summary>
    public IReadOnlyList<ToastButton> Buttons { get; init; } = [];

    /// <summary>Stays on screen longer (about 25 seconds instead of 5).</summary>
    public bool LongDuration { get; init; }

    /// <summary>No sound.</summary>
    public bool Silent { get; init; }

    /// <summary>Identifies a toast so it can be replaced or removed (with <see cref="Group"/>). Null for one-off toasts.</summary>
    public string? Tag { get; init; }

    /// <summary>The group of <see cref="Tag"/>.</summary>
    public string? Group { get; init; }

    /// <summary>How long it stays in the notification center.</summary>
    public TimeSpan Expiration { get; init; } = TimeSpan.FromDays(1);
}
