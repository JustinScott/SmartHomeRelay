using Microsoft.Win32;
using SmartHomeRelay.Core.Toasts;
using SmartHomeRelay.Platform.Windows.Notifications;

namespace SmartHomeRelay.Platform.Tests.Notifications;

/// <summary>
/// Windows' side of toasts (spec 5.6), without showing any: the XML is accepted by Windows' own parser, and the
/// registration writes the current user's keys (under a test ID, removed afterwards).
/// </summary>
[Trait("Category", "Windows")]
public sealed class WindowsToastTests
{
    [Fact]
    public void ToastXml_LoadsInWindowsXmlDocument()
    {
        var xml = ToastXmlBuilder.Build(new ToastContent
        {
            Title = "Doorbell <&>",
            Message = "Motion \"detected\"",
            HeroImagePath = Path.Combine(Path.GetTempPath(), "a b.jpg"),
            LaunchUrl = new Uri("https://ha.local/cam?x=1&y=2"),
            Buttons = [new ToastButton("Live", "https://ha.local/live", true), new ToastButton("Stop", "shr:media_player:stop", false)],
            Silent = true,
            LongDuration = true,
        });

        var document = new global::Windows.Data.Xml.Dom.XmlDocument();
        document.LoadXml(xml);

        ((int)document.GetElementsByTagName("action").Length).ShouldBe(2);
    }

    [Fact]
    public void Registration_WritesNameAndIcon_UnderTheCurrentUser()
    {
        var id = $"SmartHomeRelay.Tests.{Guid.NewGuid():N}";
        try
        {
            ToastAppRegistration.Register(new ToastAppIdentity(id, "Smart Home Relay (test)", @"C:\icon.png"));

            using var key = Registry.CurrentUser.OpenSubKey(ToastAppRegistration.KeyPath + id);
            key.ShouldNotBeNull();
            key.GetValue("DisplayName").ShouldBe("Smart Home Relay (test)");
            key.GetValue("IconUri").ShouldBe(@"C:\icon.png");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(ToastAppRegistration.KeyPath + id, throwOnMissingSubKey: false);
        }
    }
}
