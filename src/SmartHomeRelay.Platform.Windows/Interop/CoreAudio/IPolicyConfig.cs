using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary>
/// <c>IPolicyConfig</c>: Windows' undocumented interface for changing the default audio device, the one the
/// Sound settings use (Windows 7 and later, same IID on Windows 10 and 11). Only <see cref="SetDefaultEndpoint"/>
/// is called; the earlier methods are placeholders that keep the vtable order and are never called.
/// </summary>
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat(nint deviceId, nint format);

    [PreserveSig]
    int GetDeviceFormat(nint deviceId, int isDefault, nint format);

    [PreserveSig]
    int ResetDeviceFormat(nint deviceId);

    [PreserveSig]
    int SetDeviceFormat(nint deviceId, nint endpointFormat, nint mixFormat);

    [PreserveSig]
    int GetProcessingPeriod(nint deviceId, int isDefault, nint defaultPeriod, nint minimumPeriod);

    [PreserveSig]
    int SetProcessingPeriod(nint deviceId, nint period);

    [PreserveSig]
    int GetShareMode(nint deviceId, nint mode);

    [PreserveSig]
    int SetShareMode(nint deviceId, nint mode);

    [PreserveSig]
    int GetPropertyValue(nint deviceId, int isFxStore, nint key, nint value);

    [PreserveSig]
    int SetPropertyValue(nint deviceId, int isFxStore, nint key, nint value);

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, DeviceRole role);

    [PreserveSig]
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
}
