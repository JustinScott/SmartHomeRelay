using SmartHomeRelay.Core.Abstractions;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace SmartHomeRelay.Platform.Windows.Media;

/// <summary>
/// Plays an announcement with Windows' built-in <c>Windows.Media.Playback.MediaPlayer</c> on the default output
/// (spec 5.9; no package). Its media-controls integration is turned off, so the announcement never shows up in
/// Windows' media flyout or replaces the session the media player entity mirrors.
/// </summary>
public sealed class WindowsAnnouncementPlayer : IAnnouncementPlayer
{
    /// <summary>Extra time past the media's own length before playback is given up as stuck.</summary>
    public static readonly TimeSpan StuckGrace = TimeSpan.FromSeconds(10);

    /// <summary>Longest wait for the media to open.</summary>
    public static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public async Task PlayAsync(byte[] audio, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(audio);
            await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
            writer.DetachStream();
        }

        stream.Seek(0);
        using var source = MediaSource.CreateFromStream(stream, contentType);
        using var player = new MediaPlayer { AutoPlay = false };
        player.CommandManager.IsEnabled = false;

        var opened = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaOpened += (sender, _) => opened.TrySetResult(sender.PlaybackSession.NaturalDuration);
        player.MediaEnded += (_, _) => ended.TrySetResult();
        player.MediaFailed += (_, e) =>
        {
            var error = new InvalidOperationException($"Windows couldn't play the audio ({e.Error}).");
            opened.TrySetException(error);
            ended.TrySetException(error);
        };

        player.Source = source;
        try
        {
            var duration = await opened.Task.WaitAsync(OpenTimeout, cancellationToken).ConfigureAwait(false);
            player.Play();

            // A stuck player must not keep other apps ducked forever.
            var limit = duration > TimeSpan.Zero ? duration + StuckGrace : TimeSpan.FromMinutes(30);
            await ended.Task.WaitAsync(limit, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Ended by the limit; the caller treats it as finished.
        }
        finally
        {
            player.Pause();
            player.Source = null;
        }
    }
}
