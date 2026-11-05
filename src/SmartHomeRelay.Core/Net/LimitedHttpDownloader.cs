using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Net;

/// <summary>
/// <see cref="IHttpDownloader"/> with <see cref="HttpClient"/> (spec 5.9, 12): http and https only (also after
/// redirects, at most 3), Windows' certificate checks with no bypass, a size cap enforced while reading,
/// a total timeout, and a content-type check. No cookies, no credentials.
/// </summary>
public sealed class LimitedHttpDownloader : IHttpDownloader, IDisposable
{
    private const int MaxRedirects = 3;
    private readonly HttpClient _client;

    /// <summary>Creates the downloader with its own handler.</summary>
    public LimitedHttpDownloader()
        : this(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = MaxRedirects,
            UseCookies = false,
            Credentials = null,
            AutomaticDecompression = DecompressionMethods.None,
        })
    {
    }

    /// <summary>Creates the downloader on <paramref name="handler"/> (tests).</summary>
    internal LimitedHttpDownloader(HttpMessageHandler handler)
    {
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SmartHomeRelay", "1.0"));
    }

    /// <summary>True for an absolute http or https URL.</summary>
    public static bool IsAllowedUrl(Uri? url) =>
        url is { IsAbsoluteUri: true } && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);

    /// <inheritdoc />
    public async Task<DownloadedFile> DownloadAsync(Uri url, DownloadLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(limits);
        if (!IsAllowedUrl(url))
        {
            throw new DownloadRejectedException("Only http and https URLs are allowed.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limits.Timeout);
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);

            if (!IsAllowedUrl(response.RequestMessage?.RequestUri))
            {
                throw new DownloadRejectedException("The server redirected to a URL that isn't http or https.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DownloadRejectedException($"The server answered {(int)response.StatusCode}.");
            }

            var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
            if (!type.StartsWith(limits.AcceptedTypePrefix, StringComparison.Ordinal))
            {
                throw new DownloadRejectedException($"The server sent '{(type.Length == 0 ? "no type" : type)}', not {limits.AcceptedTypePrefix}*.");
            }

            if (response.Content.Headers.ContentLength > limits.MaxBytes)
            {
                throw new DownloadRejectedException($"The file is larger than {limits.MaxBytes} bytes.");
            }

            var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > limits.MaxBytes)
                    {
                        throw new DownloadRejectedException($"The file is larger than {limits.MaxBytes} bytes.");
                    }

                    buffer.Write(chunk, 0, read);
                }

                return new DownloadedFile(buffer.ToArray(), type);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new DownloadRejectedException($"The download took longer than {limits.Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            throw new DownloadRejectedException("The server's certificate isn't trusted by Windows.", ex);
        }
        catch (HttpRequestException ex)
        {
            // HttpRequestException messages can contain the URL; keep only the error kind.
            throw new DownloadRejectedException($"The download failed ({ex.HttpRequestError}).", ex);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
