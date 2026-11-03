using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary><c>D3DKMT_CLOSEADAPTER</c> (d3dkmthk.h).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtCloseAdapter
{
    public uint Adapter;
}
