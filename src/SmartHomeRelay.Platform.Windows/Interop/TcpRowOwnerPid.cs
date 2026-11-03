using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary><c>MIB_TCPROW_OWNER_PID</c> (IPv4): 24 bytes. Address and port are in network byte order.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TcpRowOwnerPid
{
    public uint State;
    public uint LocalAddr;
    public uint LocalPort;
    public uint RemoteAddr;
    public uint RemotePort;
    public uint OwningPid;
}
