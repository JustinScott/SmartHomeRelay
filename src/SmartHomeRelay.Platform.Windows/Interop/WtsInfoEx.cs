using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary>Win32 <c>WTSINFOEXW</c>: a level number followed by (8-byte aligned) level-1 data.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WtsInfoEx
{
    public uint Level;
    public WtsInfoExLevel1 Data;
}
