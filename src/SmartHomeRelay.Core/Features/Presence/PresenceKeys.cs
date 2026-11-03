namespace SmartHomeRelay.Core.Features.Presence;

/// <summary>Presence entity keys (spec 5.1). Shipped keys are permanent: never rename them.</summary>
public static class PresenceKeys
{
    /// <summary>binary_sensor: ON while the session is locked.</summary>
    public const string SessionLocked = "session_locked";

    /// <summary>sensor: the signed-in user name.</summary>
    public const string LoggedInUser = "logged_in_user";

    /// <summary>sensor: seconds since the last keyboard or mouse input.</summary>
    public const string IdleSeconds = "idle_seconds";

    /// <summary>binary_sensor: ON when unlocked and idle less than the threshold.</summary>
    public const string UserActive = "user_active";

    /// <summary>sensor (enum): <c>active</c>, <c>idle</c>, or <c>locked</c>.</summary>
    public const string PresenceState = "presence_state";
}
