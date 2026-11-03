using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary><c>VOLUME_DISK_EXTENTS</c> with room for one extent: 32 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VolumeDiskExtents
{
    public uint NumberOfDiskExtents;
    public DiskExtent Extent;
}
