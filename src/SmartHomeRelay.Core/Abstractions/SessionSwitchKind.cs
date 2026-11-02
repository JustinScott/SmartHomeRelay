namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Interactive session changes (spec 5.1, 6). Mirrors Windows' session switch reasons.</summary>
public enum SessionSwitchKind
{
    /// <summary>The session was locked.</summary>
    Lock,

    /// <summary>The session was unlocked.</summary>
    Unlock,

    /// <summary>A user signed in.</summary>
    Logon,

    /// <summary>A user signed out.</summary>
    Logoff,

    /// <summary>The session was connected from the local console.</summary>
    ConsoleConnect,

    /// <summary>The session was disconnected from the local console.</summary>
    ConsoleDisconnect,

    /// <summary>The session was connected from a remote (RDP) client.</summary>
    RemoteConnect,

    /// <summary>The session was disconnected from a remote (RDP) client.</summary>
    RemoteDisconnect,

    /// <summary>Any other change (e.g. remote control status).</summary>
    Other,
}
