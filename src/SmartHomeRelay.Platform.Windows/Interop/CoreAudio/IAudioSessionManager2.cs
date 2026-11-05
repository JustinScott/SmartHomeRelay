using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary>
/// <c>IAudioSessionManager2</c> (audiopolicy.h). Declared in vtable order up to the method used; the
/// inherited <c>IAudioSessionManager</c> methods are placeholders that are never called.
/// </summary>
[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(nint sessionGuid, int streamFlags, out nint sessionControl);

    void GetSimpleAudioVolume(nint sessionGuid, int streamFlags, out nint audioVolume);

    IAudioSessionEnumerator GetSessionEnumerator();
}
