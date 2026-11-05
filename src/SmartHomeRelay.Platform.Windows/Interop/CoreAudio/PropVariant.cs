using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary>
/// <c>PROPVARIANT</c>, only as far as reading a string: the type at byte 0, the value at byte 8. 24 bytes on
/// x64. Always release with <c>PropVariantClear</c>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    /// <summary><c>VT_LPWSTR</c>.</summary>
    public const ushort VtLpwstr = 31;

    [FieldOffset(0)]
    public ushort VarType;

    [FieldOffset(8)]
    public nint Pointer;
}
