using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary>Class IDs, constants, and helpers for Windows Core Audio. No elevation needed for any of it.</summary>
internal static partial class CoreAudioMethods
{
    /// <summary><c>CLSID_MMDeviceEnumerator</c>.</summary>
    public static readonly Guid DeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    /// <summary><c>CLSID_PolicyConfigClient</c> (undocumented; see <see cref="IPolicyConfig"/>).</summary>
    public static readonly Guid PolicyConfigClassId = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    /// <summary><c>DEVICE_STATE_ACTIVE</c>.</summary>
    public const int DeviceStateActive = 0x1;

    /// <summary><c>STGM_READ</c>.</summary>
    public const int StorageRead = 0;

    /// <summary><c>CLSCTX_ALL</c>.</summary>
    public const int ClassContextAll = 0x17;

    /// <summary><c>E_NOTFOUND</c>: no default device for that role.</summary>
    public const int ErrorNotFound = unchecked((int)0x80070490);

    /// <summary>Creates a COM object by class ID.</summary>
    public static T Create<T>(Guid classId) =>
        (T)(Activator.CreateInstance(Type.GetTypeFromCLSID(classId, throwOnError: true)!)
            ?? throw new InvalidOperationException($"Could not create {typeof(T).Name}."));

    /// <summary>The default endpoint for <paramref name="flow"/>, or null if there is none.</summary>
    public static IMMDevice? TryGetDefault(IMMDeviceEnumerator enumerator, DataFlow flow)
    {
        var hr = enumerator.GetDefaultAudioEndpoint(flow, DeviceRole.Console, out var device);
        if (hr == ErrorNotFound)
        {
            return null;
        }

        Marshal.ThrowExceptionForHR(hr);
        return device;
    }

    /// <summary>The endpoint's volume control.</summary>
    public static IAudioEndpointVolume ActivateVolume(IMMDevice device)
    {
        var interfaceId = typeof(IAudioEndpointVolume).GUID;
        return (IAudioEndpointVolume)device.Activate(ref interfaceId, ClassContextAll, 0);
    }

    /// <summary>The name Windows shows for the device.</summary>
    public static string FriendlyName(IMMDevice device)
    {
        var store = device.OpenPropertyStore(StorageRead);
        var key = PropertyKey.DeviceFriendlyName;
        store.GetValue(ref key, out var value);
        try
        {
            return value.VarType == PropVariant.VtLpwstr && value.Pointer != 0
                ? Marshal.PtrToStringUni(value.Pointer) ?? string.Empty
                : string.Empty;
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(ref PropVariant value);
}
