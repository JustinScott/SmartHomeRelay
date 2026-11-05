namespace SmartHomeRelay.Core.Abstractions;

/// <summary>What Windows' media controls show for the current session (spec 5.9).</summary>
/// <param name="Status">Playback status.</param>
/// <param name="Title">Track title, if any.</param>
/// <param name="Artist">Artist, if any.</param>
/// <param name="Album">Album title, if any.</param>
/// <param name="Duration">Track length, if known.</param>
/// <param name="Position">Current position (already advanced to now while playing), if known.</param>
/// <param name="MediaType"><c>music</c>, <c>video</c> or <c>image</c>, if known.</param>
public sealed record MediaSessionInfo(
    MediaPlaybackStatus Status,
    string? Title,
    string? Artist,
    string? Album,
    TimeSpan? Duration,
    TimeSpan? Position,
    string? MediaType);
