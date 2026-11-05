using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary><c>PROPERTYKEY</c>: 20 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    /// <summary><c>PKEY_Device_FriendlyName</c>, e.g. "Speakers (Realtek(R) Audio)".</summary>
    public static readonly PropertyKey DeviceFriendlyName = new() { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), PropertyId = 14 };

    public Guid FormatId;
    public int PropertyId;
}
