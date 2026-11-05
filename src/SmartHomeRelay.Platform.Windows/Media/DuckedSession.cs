namespace SmartHomeRelay.Platform.Windows.Media;

/// <summary>One app session lowered for an announcement, as saved to disk (spec 5.9).</summary>
/// <param name="SessionId">Windows' session identifier: stable per app and device, survives the app restarting.</param>
/// <param name="InstanceId">Windows' session instance identifier: this run of the app only.</param>
/// <param name="Original">The session's volume before ducking, 0 to 1.</param>
/// <param name="Ducked">The volume it was lowered to.</param>
internal sealed record DuckedSession(string SessionId, string InstanceId, float Original, float Ducked)
{
    /// <summary>Closer than this to <see cref="Ducked"/> means nobody touched it.</summary>
    public const float Tolerance = 0.005f;

    /// <summary>True when the session is still at the level we set, so restoring it doesn't undo the user's change.</summary>
    public bool IsUntouched(float current) => Math.Abs(current - Ducked) < Tolerance;
}
