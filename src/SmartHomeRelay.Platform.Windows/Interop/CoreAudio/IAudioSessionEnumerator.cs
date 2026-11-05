using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary><c>IAudioSessionEnumerator</c> (audiopolicy.h). Every method is declared, in vtable order.</summary>
[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    int GetCount();

    /// <summary>Returns the session's <c>IAudioSessionControl</c>; cast it to <see cref="IAudioSessionControl2"/>.</summary>
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object GetSession(int index);
}
