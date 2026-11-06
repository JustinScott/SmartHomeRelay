namespace SmartHomeRelay.Core.Abstractions;

/// <summary>The per-user "start with Windows" entry (spec 7.1, 8).</summary>
public interface IStartupRegistration
{
    /// <summary>
    /// True when the app runs from its installed folder. Only then is the entry changed, so running a build from
    /// elsewhere (e.g. a development folder) never points sign-in at that copy.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>Creates or removes the entry. Does nothing when not <see cref="IsInstalled"/>. Returns true if it changed something.</summary>
    bool Apply(bool startWithWindows);
}
