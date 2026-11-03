using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary><c>D3DKMT_ADAPTERINFO</c> (d3dkmthk.h): 20 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtAdapterInfo
{
    /// <summary>Kernel adapter handle; must be closed with D3DKMTCloseAdapter.</summary>
    public uint Adapter;
    public uint LuidLowPart;
    public int LuidHighPart;
    public uint NumOfSources;
    public int PrecisePresentRegionsPreferred;
}
