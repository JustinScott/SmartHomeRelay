namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Plays downloaded audio on the default output device (spec 5.9).</summary>
public interface IAnnouncementPlayer
{
    /// <summary>
    /// Plays <paramref name="audio"/> and completes when it ends. Cancelling stops playback. Throws if the
    /// audio can't be played.
    /// </summary>
    Task PlayAsync(byte[] audio, string contentType, CancellationToken cancellationToken);
}
