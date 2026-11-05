using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary><c>IPropertyStore</c> (read-only use).</summary>
[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    int GetCount();

    PropertyKey GetAt(int index);

    void GetValue(ref PropertyKey key, out PropVariant value);
}
