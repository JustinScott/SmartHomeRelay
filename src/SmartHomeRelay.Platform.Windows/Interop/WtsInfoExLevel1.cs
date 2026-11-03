using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary>
/// Win32 <c>WTSINFOEX_LEVEL1_W</c>. Declared in full (not read by a hand-counted offset) because its
/// 8-byte <c>LARGE_INTEGER</c> fields make it 8-byte aligned, which adds padding in front of it inside
/// <see cref="WtsInfoEx"/>. A hand-counted offset got this wrong once and read <c>SessionState</c>
/// (0 = active) as if it were <c>SessionFlags</c> (0 = locked).
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WtsInfoExLevel1
{
    public uint SessionId;
    public int SessionState;

    /// <summary><c>WTS_SESSIONSTATE_LOCK</c> (0), <c>WTS_SESSIONSTATE_UNLOCK</c> (1), or unknown (-1).</summary>
    public int SessionFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string WinStationName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string UserName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 18)]
    public string DomainName;

    public long LogonTime;
    public long ConnectTime;
    public long DisconnectTime;
    public long LastInputTime;
    public long CurrentTime;
    public uint IncomingBytes;
    public uint OutgoingBytes;
    public uint IncomingFrames;
    public uint OutgoingFrames;
    public uint IncomingCompressedBytes;
    public uint OutgoingCompressedBytes;
}
