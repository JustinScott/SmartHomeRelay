namespace SmartHomeRelay.Core.Abstractions;

/// <summary>
/// Lowers other apps' audio while an announcement plays (spec 5.9). The original levels are saved before
/// anything changes, so they can be restored after a crash or sleep.
/// </summary>
public interface IAudioDucker
{
    /// <summary>Fades every other app's session on the default output to <paramref name="fraction"/> of its own volume.</summary>
    Task DuckAsync(double fraction, CancellationToken cancellationToken);

    /// <summary>Fades the ducked sessions back. A session whose volume the user changed meanwhile is left alone.</summary>
    Task RestoreAsync(CancellationToken cancellationToken);

    /// <summary>Restores levels saved by an earlier duck that never finished (app stopped, PC slept).</summary>
    Task RecoverAsync(CancellationToken cancellationToken);
}
