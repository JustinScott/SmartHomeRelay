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

    /// <summary><c>KMTQAITYPE_ADAPTERPERFDATA</c>: fills a <see cref="D3dkmtAdapterPerfData"/>.</summary>
    public const int KmtqaitypeAdapterPerfData = 62;

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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LockWorkStation();

    /// <summary>Sleeps (<paramref name="hibernate"/> false) or hibernates. Blocks until the system resumes.</summary>
    [LibraryImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool force,
        [MarshalAs(UnmanagedType.U1)] bool wakeupEventsDisabled);

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool IsPwrHibernateAllowed();

    /// <summary>Cumulative idle, kernel (including idle), and user time, as FILETIME (100 ns units).</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>Enumerates GPU adapters, opening a handle to each. Returns an NTSTATUS (0 = success).</summary>
    [LibraryImport("gdi32.dll")]
    public static partial int D3DKMTEnumAdapters2(ref D3dkmtEnumAdapters2 enumAdapters);

    /// <summary>Queries adapter information. Returns an NTSTATUS (0 = success).</summary>
    [LibraryImport("gdi32.dll")]
    public static partial int D3DKMTQueryAdapterInfo(ref D3dkmtQueryAdapterInfo query);

    /// <summary>Closes an adapter handle from D3DKMTEnumAdapters2. Returns an NTSTATUS (0 = success).</summary>
    [LibraryImport("gdi32.dll")]
    public static partial int D3DKMTCloseAdapter(ref D3dkmtCloseAdapter close);
}
