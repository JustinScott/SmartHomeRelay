using SmartHomeRelay.Core.Toasts;

namespace SmartHomeRelay.Core.Tests.Toasts;

/// <summary>Spec 5.6: the toast XML.</summary>
public sealed class ToastXmlBuilderTests
{
    [Fact]
    public void FullToast()
    {
        var image = Path.Combine(Path.GetTempPath(), "shr", "a.jpg");
        var xml = ToastXmlBuilder.Build(new ToastContent
        {
            Title = "Doorbell",
            Message = "Motion detected",
            HeroImagePath = image,
            LaunchUrl = new Uri("https://ha.local/dashboard"),
            Buttons = [new ToastButton("Live feed", "https://ha.local/cam", OpensUrl: true)],
        });

        xml.ShouldBe(
            "<toast launch=\"https://ha.local/dashboard\" activationType=\"protocol\"><visual><binding template=\"ToastGeneric\">"
            + "<text>Doorbell</text><text>Motion detected</text>"
            + $"<image placement=\"hero\" src=\"{new Uri(image).AbsoluteUri}\" />"
            + "</binding></visual><actions><action content=\"Live feed\" arguments=\"https://ha.local/cam\" activationType=\"protocol\" /></actions></toast>");
    }

    [Fact]
    public void PayloadText_IsEscaped_NeverMarkup()
    {
        var xml = ToastXmlBuilder.Build(new ToastContent { Title = "<b>x</b>", Message = "a & \"b\"" });

        xml.ShouldContain("<text>&lt;b&gt;x&lt;/b&gt;</text>");
        xml.ShouldContain("<text>a &amp; \"b\"</text>");
    }

    [Fact]
    public void SilentLongToast_WithAnAppButton()
    {
        var xml = ToastXmlBuilder.Build(new ToastContent
        {
            Title = "Home Assistant is speaking",
            Buttons = [new ToastButton("Stop", "shr:media_player:stop", OpensUrl: false)],
            Silent = true,
            LongDuration = true,
        });

        xml.ShouldContain("<toast duration=\"long\">");
        xml.ShouldContain("activationType=\"foreground\"");
        xml.ShouldEndWith("<audio silent=\"true\" /></toast>");
    }

    [Fact]
    public void StayOnScreen_IsTheReminderScenario_OnlyWithAButton()
    {
        var withButton = ToastXmlBuilder.Build(new ToastContent
        {
            Title = "T",
            Buttons = [new ToastButton("Stop", "shr:media_player:stop", OpensUrl: false)],
            StayOnScreen = true,
        });
        var withoutButton = ToastXmlBuilder.Build(new ToastContent { Title = "T", StayOnScreen = true });

        withButton.ShouldStartWith("<toast scenario=\"reminder\">");
        withoutButton.ShouldNotContain("scenario");
    }

    [Fact]
    public void AtMostFiveButtons_AndEmptyTitleIsLeftOut()
    {
        var buttons = Enumerable.Range(1, 7).Select(i => new ToastButton($"B{i}", $"https://x/{i}", true)).ToList();

        var xml = ToastXmlBuilder.Build(new ToastContent { Message = "Hi", Buttons = buttons });

        xml.Split("<action ").Length.ShouldBe(6);
        xml.ShouldContain("<binding template=\"ToastGeneric\"><text>Hi</text></binding>");
    }
}
