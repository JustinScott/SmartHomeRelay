namespace SmartHomeRelay.Core.Net;

/// <summary>Limits for one download.</summary>
/// <param name="MaxBytes">Largest accepted file.</param>
/// <param name="Timeout">Longest the whole download may take.</param>
/// <param name="AcceptedTypePrefix">The media type must start with this, e.g. <c>audio/</c>.</param>
public sealed record DownloadLimits(long MaxBytes, TimeSpan Timeout, string AcceptedTypePrefix);
