using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop;

/// <summary>Win32 <c>LASTINPUTINFO</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LastInputInfo
{
    /// <summary>Must be set to the struct size before calling GetLastInputInfo.</summary>
    public uint Size;

    /// <summary>Tick count (GetTickCount, milliseconds since boot, wraps every ~49.7 days) of the last input.</summary>
    public uint Time;
}
