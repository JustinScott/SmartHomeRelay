namespace SmartHomeRelay.Core.Mqtt;

/// <summary>
/// Exponential backoff with jitter: about 1s, 2s, 4s ... capped at 60s (spec 4.3). Jitter (±20%) keeps
/// several PCs from hammering the broker in lockstep after it restarts.
/// </summary>
public sealed class ReconnectBackoff(Random random)
{
    /// <summary>The first delay before jitter.</summary>
    public static readonly TimeSpan Initial = TimeSpan.FromSeconds(1);

    /// <summary>The largest delay, after jitter.</summary>
    public static readonly TimeSpan Maximum = TimeSpan.FromSeconds(60);

    private const double JitterFraction = 0.2;

    /// <summary>Delay before retry number <paramref name="attempt"/> (0-based).</summary>
    public TimeSpan GetDelay(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);

        // Cap the exponent so the multiplication can't overflow on very long outages.
        var baseSeconds = Math.Min(Maximum.TotalSeconds, Initial.TotalSeconds * Math.Pow(2, Math.Min(attempt, 16)));
        var factor = 1 + (((random.NextDouble() * 2) - 1) * JitterFraction);
        return TimeSpan.FromSeconds(Math.Min(Maximum.TotalSeconds, baseSeconds * factor));
    }
}
