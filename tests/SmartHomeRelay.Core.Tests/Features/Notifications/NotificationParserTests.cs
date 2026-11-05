using SmartHomeRelay.Core.Features.Notifications;

namespace SmartHomeRelay.Core.Tests.Features.Notifications;

/// <summary>Spec 5.6: the prototype's payload contract.</summary>
public sealed class NotificationParserTests
{
    [Fact]
    public void PrototypePayload_IsUnchanged()
    {
        var result = NotificationParser.Parse("""
            { "Title": "Doorbell", "Message": "Motion detected", "HeroImageURL": "https://ha.local/snap.jpg", "ActivationURL": "https://ha.local/cam", "Audio": "" }
            """);

        result.Problems.ShouldBeEmpty();
        var request = result.Request.ShouldNotBeNull();
        request.Title.ShouldBe("Doorbell");
        request.Message.ShouldBe("Motion detected");
        request.HeroImageUrl!.AbsoluteUri.ShouldBe("https://ha.local/snap.jpg");
        request.ActivationUrl!.AbsoluteUri.ShouldBe("https://ha.local/cam");
    }

    [Fact]
    public void FieldNames_IgnoreLetterCase()
    {
        var request = NotificationParser.Parse("""{ "title": "T", "MESSAGE": "M", "heroImageUrl": "http://x/a.png" }""").Request.ShouldNotBeNull();

        request.Title.ShouldBe("T");
        request.Message.ShouldBe("M");
        request.HeroImageUrl.ShouldNotBeNull();
    }

    [Fact]
    public void PlainText_IsTheMessage_AsTheNotifyEntitySendsIt()
    {
        var request = NotificationParser.Parse("  Washer is done  ").Request.ShouldNotBeNull();

        request.Message.ShouldBe("Washer is done");
        request.Title.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ \"Title\": ")]
    [InlineData("{ \"Title\": \"\", \"Message\": \"\" }")]
    [InlineData("{ \"Audio\": \"x\" }")]
    public void NothingToShow_IsIgnored_WithAProblem(string payload)
    {
        var result = NotificationParser.Parse(payload);

        result.Request.ShouldBeNull();
        result.Problems.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("snapshot.jpg")]
    [InlineData(@"C:\\Windows\\win.ini")]
    [InlineData(@"\\\\server\\share\\a.jpg")]
    [InlineData("file:///C:/a.jpg")]
    [InlineData("ftp://ha/a.jpg")]
    [InlineData("vlc://ha/a.mp4")]
    public void NonHttpUrls_AreDropped_WithoutTouchingFiles(string url)
    {
        var result = NotificationParser.Parse($$"""{ "Title": "T", "HeroImageURL": "{{url}}", "ActivationURL": "{{url}}" }""");

        var request = result.Request.ShouldNotBeNull("the toast still shows");
        request.HeroImageUrl.ShouldBeNull();
        request.ActivationUrl.ShouldBeNull();
        result.Problems.Count.ShouldBe(2);
    }

    [Fact]
    public void Buttons_ValidKept_InvalidDropped_ExtrasTruncated()
    {
        var result = NotificationParser.Parse("""
            { "Title": "T", "Buttons": [
                { "Label": "A", "URL": "https://x/a" },
                { "Label": "", "URL": "https://x/b" },
                { "Label": "C", "URL": "file:///c" },
                { "Label": "D", "ActionID": "open" },
                "nope",
                { "label": "E", "url": "http://x/e" },
                { "Label": "F", "URL": "https://x/f" },
                { "Label": "G", "URL": "https://x/g" },
                { "Label": "H", "URL": "https://x/h" },
                { "Label": "I", "URL": "https://x/i" }
            ] }
            """);

        result.Request!.Buttons.Select(b => b.Label).ShouldBe(["A", "E", "F", "G", "H"]);
        result.Problems.ShouldContain(p => p.Contains("ActionID", StringComparison.Ordinal));
        result.Problems.ShouldContain(p => p.Contains("only the first 5", StringComparison.Ordinal));
    }

    [Fact]
    public void NoValidButtons_StillShowsTheToast()
    {
        NotificationParser.Parse("""{ "Message": "M", "Buttons": [ { "Label": "x" } ] }""").Request!.Buttons.ShouldBeEmpty();
    }

    [Fact]
    public void EventId_IsFlagged_AndOtherwiseIgnored()
    {
        var result = NotificationParser.Parse("""{ "Title": "T", "EventID": "1700000000.123-abc" }""");

        result.HadEventId.ShouldBeTrue();
        result.Request.ShouldNotBeNull();
    }

    [Fact]
    public void WrongTypes_AreIgnored_NotThrown()
    {
        var result = NotificationParser.Parse("""{ "Title": 5, "Message": "M", "Buttons": "x", "HeroImageURL": true, "Unknown": [1] }""");

        result.Request!.Title.ShouldBeEmpty();
        result.Problems.Count.ShouldBe(3);
    }

    [Fact]
    public void Problems_NeverQuoteThePayload()
    {
        var result = NotificationParser.Parse("""{ "Title": "secret-title", "HeroImageURL": "C:\\secret\\path.jpg" }""");

        result.Problems.ShouldAllBe(p => !p.Contains("secret", StringComparison.Ordinal));
    }
}
