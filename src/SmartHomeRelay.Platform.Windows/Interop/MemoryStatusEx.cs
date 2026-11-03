using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary>Win32 <c>MEMORYSTATUSEX</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MemoryStatusEx
{
    /// <summary>Must be set to the struct size before calling GlobalMemoryStatusEx.</summary>
    public uint Length;
    public uint MemoryLoad;
    public ulong TotalPhys;
    public ulong AvailPhys;
    public ulong TotalPageFile;
    public ulong AvailPageFile;
    public ulong TotalVirtual;
    public ulong AvailVirtual;
    public ulong AvailExtendedVirtual;
}
