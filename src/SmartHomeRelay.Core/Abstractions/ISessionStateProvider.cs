namespace SmartHomeRelay.Core.Abstractions;

/// <summary>
/// The current state of the interactive session the app runs in (spec 5.1). Used for the initial values;
/// changes afterwards arrive as <see cref="ISystemEventSource.SessionSwitched"/> events, not by polling.
/// </summary>
public interface ISessionStateProvider
{
    /// <summary>True if the session is locked right now.</summary>
    bool IsLocked();

    /// <summary>The signed-in user's name, e.g. <c>Justin</c>.</summary>
    string GetUserName();
}
