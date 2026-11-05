namespace SmartHomeRelay.Core.Abstractions;

/// <summary>
/// The current Windows media session, the one Windows' media controls show (spec 5.9). Standard user.
/// </summary>
public interface IMediaSessionSource
{
    /// <summary>Raised when the session, its playback state, its media, or its timeline may have changed. Any thread.</summary>
    event EventHandler? Changed;

    /// <summary>The current session, or null when there is none.</summary>
    Task<MediaSessionInfo?> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>Sends a transport command to the current session. False if there is none or it refused.</summary>
    Task<bool> SendAsync(MediaSessionCommand command, CancellationToken cancellationToken);

    /// <summary>Moves the current session to <paramref name="position"/>. False if there is none or it refused.</summary>
    Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken);
}
