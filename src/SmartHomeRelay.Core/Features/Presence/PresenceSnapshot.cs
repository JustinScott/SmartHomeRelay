using System.Globalization;

namespace SmartHomeRelay.Core.Features.Presence;

/// <summary>Everything the Presence module publishes, at one moment.</summary>
/// <param name="Locked">Whether the session is locked.</param>
/// <param name="UserName">The signed-in user.</param>
/// <param name="ReportedIdleSeconds">Idle time as published (stepped, see <see cref="PresenceCalculator.ReportIdleSeconds"/>).</param>
/// <param name="UserActive">Unlocked and idle less than the threshold.</param>
/// <param name="State">One of <see cref="PresenceStates"/>.</param>
public sealed record PresenceSnapshot(bool Locked, string UserName, long ReportedIdleSeconds, bool UserActive, string State)
{
    /// <summary>The MQTT state payload for each entity key.</summary>
    public IReadOnlyDictionary<string, string> ToPayloads() => new Dictionary<string, string>
    {
        [PresenceKeys.SessionLocked] = Locked ? "ON" : "OFF",
        [PresenceKeys.LoggedInUser] = UserName,
        [PresenceKeys.IdleSeconds] = ReportedIdleSeconds.ToString(CultureInfo.InvariantCulture),
        [PresenceKeys.UserActive] = UserActive ? "ON" : "OFF",
        [PresenceKeys.PresenceState] = State,
    };
}
