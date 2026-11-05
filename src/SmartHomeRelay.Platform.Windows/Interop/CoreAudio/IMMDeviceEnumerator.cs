using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary><c>IMMDeviceEnumerator</c> (mmdeviceapi.h). Methods in vtable order.</summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    IMMDeviceCollection EnumAudioEndpoints(DataFlow dataFlow, int stateMask);

    /// <summary>Throws (<c>E_NOTFOUND</c>) when there is no such device; use <see cref="CoreAudioMethods.TryGetDefault"/>.</summary>
    [PreserveSig]
    int GetDefaultAudioEndpoint(DataFlow dataFlow, DeviceRole role, out IMMDevice? device);

    IMMDevice GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id);

    void RegisterEndpointNotificationCallback(IMMNotificationClient client);

    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}
