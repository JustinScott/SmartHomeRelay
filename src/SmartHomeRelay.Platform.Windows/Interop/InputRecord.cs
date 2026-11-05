using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary>
/// <c>INPUT</c> for mouse input: the type, then the union at byte 8 (40 bytes on x64). Only the mouse member
/// is used; the union's size comes from <c>MOUSEINPUT</c>, its largest member.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct InputRecord
{
    /// <summary><c>INPUT_MOUSE</c>.</summary>
    public const uint TypeMouse = 0;

    public uint Type;
    public MouseInput Mouse;
}
