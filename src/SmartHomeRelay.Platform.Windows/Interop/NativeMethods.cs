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

    /// <summary><c>AF_INET</c>.</summary>
    public const int AfInet = 2;

    /// <summary><c>TCP_TABLE_OWNER_PID_LISTENER</c>.</summary>
    public const int TcpTableOwnerPidListener = 3;

    /// <summary><c>ERROR_INSUFFICIENT_BUFFER</c>.</summary>
    public const uint ErrorInsufficientBuffer = 122;

    /// <summary><c>PROCESS_QUERY_LIMITED_INFORMATION</c>: allowed for other users' and elevated processes.</summary>
    public const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary><c>IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS</c>.</summary>
    public const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;

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

    /// <summary>Lists TCP listeners with their owning process IDs. Returns a Win32 error code (0 = success).</summary>
    [LibraryImport("iphlpapi.dll")]
    public static partial uint GetExtendedTcpTable(nint table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int addressFamily, int tableClass, uint reserved);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool QueryFullProcessImageName(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint flags, char* buffer, ref int size);

    /// <summary>Opens a volume with no access rights (enough for the disk-extents query).</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string fileName, uint access, uint shareMode, nint securityAttributes, uint creationDisposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle device, uint controlCode, nint inBuffer, uint inSize, out VolumeDiskExtents outBuffer, uint outSize, out uint bytesReturned, nint overlapped);
}
