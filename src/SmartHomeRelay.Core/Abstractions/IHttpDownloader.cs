namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Downloads a file from an http(s) URL into memory, within limits (spec 5.9, 12).</summary>
public interface IHttpDownloader
{
    /// <summary>
    /// Downloads <paramref name="url"/>. Throws <see cref="Net.DownloadRejectedException"/> when the URL isn't
    /// http(s), the server fails, the type isn't accepted, the file is too big, or it takes too long.
    /// </summary>
    Task<Net.DownloadedFile> DownloadAsync(Uri url, Net.DownloadLimits limits, CancellationToken cancellationToken);
}
