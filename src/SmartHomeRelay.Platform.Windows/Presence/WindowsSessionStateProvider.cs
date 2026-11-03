using System.Diagnostics;
using System.Runtime.InteropServices;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Interop;

namespace SmartHomeRelay.Platform.Windows.Presence;

/// <summary>
/// Lock state from <c>WTSQuerySessionInformation(WTSSessionInfoEx)</c>, read for the app's own session.
/// Only used for initial values and after wake; changes otherwise arrive as session switch events.
/// </summary>
public sealed class WindowsSessionStateProvider : ISessionStateProvider
{
    /// <inheritdoc />
    public bool IsLocked()
    {
        int sessionId;
        using (var process = Process.GetCurrentProcess())
        {
            sessionId = process.SessionId;
        }

        if (!NativeMethods.WTSQuerySessionInformation(
                NativeMethods.WtsCurrentServerHandle, sessionId, NativeMethods.WtsSessionInfoEx, out var buffer, out var bytes))
        {
            // Can't tell: assume unlocked (the app normally starts at sign-in, unlocked). A lock event corrects it.
            return false;
        }

        try
        {
            if (bytes < Marshal.SizeOf<WtsInfoEx>())
            {
                return false;
            }

            var info = Marshal.PtrToStructure<WtsInfoEx>(buffer);
            return info.Level == 1 && IsLockedFlag(info.Data.SessionFlags);
        }
        finally
        {
            NativeMethods.WTSFreeMemory(buffer);
        }
    }

    /// <inheritdoc />
    public string GetUserName() => Environment.UserName;

    /// <summary>
    /// Interprets <c>SessionFlags</c>. Only an explicit lock counts as locked; unknown (-1) is treated as
    /// unlocked, and a lock event corrects it.
    /// </summary>
    internal static bool IsLockedFlag(int sessionFlags) => sessionFlags == NativeMethods.WtsSessionStateLock;
}
