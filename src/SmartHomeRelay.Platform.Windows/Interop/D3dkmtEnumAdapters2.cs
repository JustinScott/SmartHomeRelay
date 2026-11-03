using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary><c>D3DKMT_ENUMADAPTERS2</c> (d3dkmthk.h). Call once with a null buffer to get the count.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtEnumAdapters2
{
    public uint NumAdapters;

    /// <summary>Pointer to an array of <see cref="D3dkmtAdapterInfo"/>.</summary>
    public nint Adapters;
}
