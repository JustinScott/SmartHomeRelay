using System.Xml.Linq;

namespace SmartHomeRelay.Core.Toasts;

/// <summary>
/// Builds Windows toast XML (the ToastGeneric template) from <see cref="ToastContent"/> (spec 5.6). Pure and
/// escaped by construction, so payload text can never inject markup. URLs open through protocol activation:
/// Windows opens them itself, even when the app isn't running.
/// </summary>
public static class ToastXmlBuilder
{
    /// <summary>Windows shows at most 5 buttons.</summary>
    public const int MaxButtons = 5;

    /// <summary>Builds the XML.</summary>
    public static string Build(ToastContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var toast = new XElement("toast");
        if (content.LaunchUrl is { } launch)
        {
            toast.Add(new XAttribute("launch", launch.AbsoluteUri), new XAttribute("activationType", "protocol"));
        }

        if (content.LongDuration)
        {
            toast.Add(new XAttribute("duration", "long"));
        }

        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"));
        if (content.Title.Length > 0)
        {
            binding.Add(new XElement("text", content.Title));
        }

        if (content.Message.Length > 0)
        {
            binding.Add(new XElement("text", content.Message));
        }

        if (content.HeroImagePath is { } image)
        {
            binding.Add(new XElement("image", new XAttribute("placement", "hero"), new XAttribute("src", new Uri(Path.GetFullPath(image)).AbsoluteUri)));
        }

        toast.Add(new XElement("visual", binding));

        if (content.Buttons.Count > 0)
        {
            var actions = new XElement("actions");
            foreach (var button in content.Buttons.Take(MaxButtons))
            {
                actions.Add(new XElement(
                    "action",
                    new XAttribute("content", button.Label),
                    new XAttribute("arguments", button.Arguments),
                    new XAttribute("activationType", button.OpensUrl ? "protocol" : "foreground")));
            }

            toast.Add(actions);
        }

        if (content.Silent)
        {
            toast.Add(new XElement("audio", new XAttribute("silent", "true")));
        }

        return toast.ToString(SaveOptions.DisableFormatting);
    }
}
