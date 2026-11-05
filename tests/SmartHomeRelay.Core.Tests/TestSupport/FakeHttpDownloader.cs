using System.Collections.Concurrent;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Net;

namespace SmartHomeRelay.Core.Tests.TestSupport;

internal sealed class FakeHttpDownloader : IHttpDownloader
{
    public ConcurrentQueue<(Uri Url, DownloadLimits Limits)> Requests { get; } = new();

    public DownloadRejectedException? Reject { get; set; }

    public Task<DownloadedFile> DownloadAsync(Uri url, DownloadLimits limits, CancellationToken cancellationToken)
    {
        Requests.Enqueue((url, limits));
        return Reject is { } ex ? Task.FromException<DownloadedFile>(ex) : Task.FromResult(new DownloadedFile(new byte[3], "audio/mpeg"));
    }
}
