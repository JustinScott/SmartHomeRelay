using System.Text.Json;
using SmartHomeRelay.Core.Net;
using SmartHomeRelay.Core.Toasts;

namespace SmartHomeRelay.Core.Features.Notifications;

/// <summary>
/// Parses the notification payload (spec 5.6), the prototype's contract: a JSON object with the fields
/// <c>Title</c>, <c>Message</c>, <c>HeroImageURL</c>, <c>ActivationURL</c>, <c>Audio</c> and <c>Buttons</c>, matched
/// ignoring letter case. A payload that isn't JSON (it doesn't start with <c>{</c>) is the message itself, which is
/// what Home Assistant's notify entity sends. Never throws: problems are returned for the log.
/// </summary>
public static class NotificationParser
{
    /// <summary>Parses <paramref name="payload"/>.</summary>
    public static NotificationParseResult Parse(string? payload)
    {
        var problems = new List<string>();
        var text = payload?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            problems.Add("The payload is empty.");
            return new(null, problems, false);
        }

        if (!text.StartsWith('{'))
        {
            return new(new NotificationRequest { Message = text }, problems, false);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            problems.Add("The payload starts with '{' but isn't valid JSON.");
            return new(null, problems, false);
        }

        using (document)
        {
            var root = document.RootElement;
            var title = ReadString(root, "Title", problems);
            var message = ReadString(root, "Message", problems);
            var hadEventId = TryGet(root, "EventID", out _);

            if (title.Length == 0 && message.Length == 0)
            {
                problems.Add("Title and Message are both empty.");
                return new(null, problems, hadEventId);
            }

            var request = new NotificationRequest
            {
                Title = title,
                Message = message,
                HeroImageUrl = ReadUrl(root, "HeroImageURL", problems),
                ActivationUrl = ReadUrl(root, "ActivationURL", problems),
                Buttons = ReadButtons(root, problems),
            };
            return new(request, problems, hadEventId);
        }
    }

    /// <summary>Finds a property ignoring letter case; the first match wins.</summary>
    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string ReadString(JsonElement root, string name, List<string> problems)
    {
        if (!TryGet(root, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            problems.Add($"{name} ignored: not text.");
            return string.Empty;
        }

        return value.GetString()?.Trim() ?? string.Empty;
    }

    private static Uri? ReadUrl(JsonElement root, string name, List<string> problems)
    {
        var text = ReadString(root, name, problems);
        if (text.Length == 0)
        {
            return null;
        }

        // Only absolute http(s) URLs (spec 12): file paths, UNC paths and other schemes are dropped, never read.
        if (Uri.TryCreate(text, UriKind.Absolute, out var url) && LimitedHttpDownloader.IsAllowedUrl(url))
        {
            return url;
        }

        problems.Add($"{name} ignored: only absolute http or https URLs are allowed.");
        return null;
    }

    private static List<NotificationButton> ReadButtons(JsonElement root, List<string> problems)
    {
        var buttons = new List<NotificationButton>();
        if (!TryGet(root, "Buttons", out var list) || list.ValueKind == JsonValueKind.Null)
        {
            return buttons;
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            problems.Add("Buttons ignored: not a list.");
            return buttons;
        }

        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            index++;
            if (buttons.Count == ToastXmlBuilder.MaxButtons)
            {
                problems.Add($"Buttons: only the first {ToastXmlBuilder.MaxButtons} are shown.");
                break;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"Button {index} dropped: not an object.");
                continue;
            }

            var label = ReadString(item, "Label", problems);
            var url = ReadUrl(item, "URL", problems);
            if (url is null && TryGet(item, "ActionID", out _))
            {
                problems.Add($"Button {index} dropped: ActionID buttons aren't supported yet.");
                continue;
            }

            if (label.Length == 0 || url is null)
            {
                problems.Add($"Button {index} dropped: it needs a Label and an http or https URL.");
                continue;
            }

            buttons.Add(new NotificationButton(label, url));
        }

        return buttons;
    }
}
