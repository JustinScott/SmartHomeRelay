using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary>
/// <c>IAudioSessionControl2</c> (audiopolicy.h), including the inherited <c>IAudioSessionControl</c> methods,
/// in vtable order.
/// </summary>
[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    /// <summary><c>AudioSessionState</c>: 0 inactive, 1 active, 2 expired.</summary>
    int GetState();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetDisplayName();

    void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetIconPath();

    void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

    Guid GetGroupingParam();

    void SetGroupingParam(ref Guid groupingId, ref Guid eventContext);

    void RegisterAudioSessionNotification(nint client);

    void UnregisterAudioSessionNotification(nint client);

    /// <summary>Stable per app and device (survives the app restarting).</summary>
    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetSessionIdentifier();

    /// <summary>Unique to this session instance.</summary>
    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetSessionInstanceIdentifier();

    uint GetProcessId();

    /// <summary>S_OK (0) for the system sounds session, S_FALSE (1) otherwise.</summary>
    [PreserveSig]
    int IsSystemSoundsSession();

    void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}
