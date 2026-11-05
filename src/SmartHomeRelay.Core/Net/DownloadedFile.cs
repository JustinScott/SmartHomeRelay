namespace SmartHomeRelay.Core.Net;

/// <summary>A downloaded file.</summary>
/// <param name="Content">The bytes.</param>
/// <param name="ContentType">The media type the server named, lowercase, without parameters, e.g. <c>audio/mpeg</c>.</param>
public sealed record DownloadedFile(byte[] Content, string ContentType);
