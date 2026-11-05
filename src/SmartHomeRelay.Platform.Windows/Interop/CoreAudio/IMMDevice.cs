using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary><c>IMMDevice</c>.</summary>
[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object Activate(ref Guid interfaceId, int classContext, nint activationParams);

    IPropertyStore OpenPropertyStore(int access);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetId();

    int GetState();
}
