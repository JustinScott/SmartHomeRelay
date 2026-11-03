using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary>All P/Invoke declarations (spec 10: P/Invoke only here). None of these need elevation.</summary>
internal static partial class NativeMethods
{
    /// <summary><c>WTSSessionInfoEx</c> from WTS_INFO_CLASS.</summary>
    public const int WtsSessionInfoEx = 25;

    /// <summary><c>WTS_SESSIONSTATE_LOCK</c>.</summary>
    public const int WtsSessionStateLock = 0;

    /// <summary><c>WTS_SESSIONSTATE_UNLOCK</c>.</summary>
    public const int WtsSessionStateUnlock = 1;

    /// <summary>The local server, for the WTS functions.</summary>
    public static readonly nint WtsCurrentServerHandle = 0;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLastInputInfo(ref LastInputInfo info);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSQuerySessionInformation(nint server, int sessionId, int infoClass, out nint buffer, out uint bytesReturned);

    [LibraryImport("wtsapi32.dll")]
    public static partial void WTSFreeMemory(nint memory);
}
