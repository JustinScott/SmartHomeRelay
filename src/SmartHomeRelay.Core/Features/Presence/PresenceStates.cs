namespace SmartHomeRelay.Core.Features.Presence;

/// <summary>Values of the <c>presence_state</c> enum sensor (spec 5.1). Part of the HA contract.</summary>
public static class PresenceStates
{
    /// <summary>Unlocked, with input within the idle threshold.</summary>
    public const string Active = "active";

    /// <summary>Unlocked, but no input for at least the idle threshold.</summary>
    public const string Idle = "idle";

    /// <summary>The session is locked.</summary>
    public const string Locked = "locked";

    /// <summary>All values, in the order HA lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Active, Idle, Locked];
}
