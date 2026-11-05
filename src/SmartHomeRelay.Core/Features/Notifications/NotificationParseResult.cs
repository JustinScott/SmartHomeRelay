namespace SmartHomeRelay.Core.Features.Notifications;

/// <summary>The outcome of parsing a notification payload.</summary>
/// <param name="Request">The notification, or null when nothing can be shown.</param>
/// <param name="Problems">Fields that were ignored or dropped, for the log (never contains URLs or text from the payload).</param>
/// <param name="HadEventId">The payload had the removed Frigate <c>EventID</c> field.</param>
public sealed record NotificationParseResult(NotificationRequest? Request, IReadOnlyList<string> Problems, bool HadEventId);
