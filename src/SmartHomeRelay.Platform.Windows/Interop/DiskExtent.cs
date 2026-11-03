using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary><c>DISK_EXTENT</c>: 24 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DiskExtent
{
    public uint DiskNumber;
    public long StartingOffset;
    public long ExtentLength;
}
