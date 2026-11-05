namespace SmartHomeRelay.Core.Features.Notifications;

/// <summary>A toast button that opens a URL (spec 5.6).</summary>
/// <param name="Label">The button text.</param>
/// <param name="Url">An absolute http(s) URL.</param>
public sealed record NotificationButton(string Label, Uri Url);
