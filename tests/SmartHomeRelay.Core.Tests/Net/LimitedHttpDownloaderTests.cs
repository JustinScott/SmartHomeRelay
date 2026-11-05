using System.Net;
using System.Net.Http.Headers;
using SmartHomeRelay.Core.Net;

namespace SmartHomeRelay.Core.Tests.Net;

/// <summary>Spec 5.9, 12: downloads stay within their limits.</summary>
public sealed class LimitedHttpDownloaderTests
{
    private static readonly DownloadLimits s_limits = new(1000, TimeSpan.FromSeconds(5), "audio/");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Audio_IsDownloaded()
    {
        using var downloader = new LimitedHttpDownloader(new StubHandler(_ => Respond(new byte[10], "audio/mpeg")));

        var file = await downloader.DownloadAsync(new Uri("http://ha.local/a.mp3"), s_limits, Ct);

        file.Content.Length.ShouldBe(10);
        file.ContentType.ShouldBe("audio/mpeg");
    }

    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("ftp://ha.local/a.mp3")]
    public async Task OtherSchemes_AreRefused_WithoutARequest(string url)
    {
        var requests = 0;
        using var downloader = new LimitedHttpDownloader(new StubHandler(_ =>
        {
            requests++;
            return Respond([], "audio/mpeg");
        }));

        await Should.ThrowAsync<DownloadRejectedException>(() => downloader.DownloadAsync(new Uri(url), s_limits, Ct));
        requests.ShouldBe(0);
    }

    [Fact]
    public async Task WrongType_IsRefused()
    {
        using var downloader = new LimitedHttpDownloader(new StubHandler(_ => Respond(new byte[10], "text/html")));

        var ex = await Should.ThrowAsync<DownloadRejectedException>(() => downloader.DownloadAsync(new Uri("http://ha.local/a"), s_limits, Ct));
        ex.Message.ShouldContain("text/html");
    }

    [Fact]
    public async Task TooBig_ByHeader_IsRefused()
    {
        using var downloader = new LimitedHttpDownloader(new StubHandler(_ => Respond(new byte[2000], "audio/mpeg")));

        await Should.ThrowAsync<DownloadRejectedException>(() => downloader.DownloadAsync(new Uri("http://ha.local/a"), s_limits, Ct));
    }

    [Fact]
    public async Task TooBig_WithoutALengthHeader_IsRefusedWhileReading()
    {
        using var downloader = new LimitedHttpDownloader(new StubHandler(_ =>
        {
            var content = new StreamContent(new MemoryStream(new byte[5000]));
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));

        await Should.ThrowAsync<DownloadRejectedException>(() => downloader.DownloadAsync(new Uri("http://ha.local/a"), s_limits, Ct));
    }

    [Fact]
    public async Task ServerError_IsRefused_WithoutTheUrlInTheMessage()
    {
        using var downloader = new LimitedHttpDownloader(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var ex = await Should.ThrowAsync<DownloadRejectedException>(() => downloader.DownloadAsync(new Uri("http://ha.local/a.mp3?authSig=secret"), s_limits, Ct));
        ex.Message.ShouldContain("403");
        ex.Message.ShouldNotContain("secret");
    }

    [Fact]
    public async Task Slow_IsRefused_AfterTheTimeout()
    {
        using var downloader = new LimitedHttpDownloader(new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Respond([], "audio/mpeg");
        }));

        var ex = await Should.ThrowAsync<DownloadRejectedException>(() =>
            downloader.DownloadAsync(new Uri("http://ha.local/a"), s_limits with { Timeout = TimeSpan.FromMilliseconds(100) }, Ct));
        ex.Message.ShouldContain("longer than");
    }

    private static HttpResponseMessage Respond(byte[] body, string type)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request)))
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await respond(request, cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }
}
