using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary><c>D3DKMT_QUERYADAPTERINFO</c> (d3dkmthk.h).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtQueryAdapterInfo
{
    public uint Adapter;

    /// <summary>A <c>KMTQUERYADAPTERINFOTYPE</c> value.</summary>
    public int Type;
    public nint PrivateDriverData;
    public uint PrivateDriverDataSize;
}
