using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary>
/// <c>D3DKMT_ADAPTER_PERFDATA</c> (d3dkmthk.h, WDDM 2.4+): the per-adapter sensors Task Manager shows.
/// 64 bytes on x64.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtAdapterPerfData
{
    public uint PhysicalAdapterIndex;
    public ulong MemoryFrequency;
    public ulong MaxMemoryFrequency;
    public ulong MaxMemoryFrequencyOC;
    public ulong MemoryBandwidth;
    public ulong PcieBandwidth;
    public uint FanRpm;

    /// <summary>Power draw in tenths of a percent of the adapter's maximum.</summary>
    public uint Power;

    /// <summary>Temperature in tenths of a degree Celsius.</summary>
    public uint Temperature;
    public byte PowerStateOverride;
}
