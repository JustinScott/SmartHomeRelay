using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary><c>ISimpleAudioVolume</c> (audioclient.h): one app session's volume. Every method, in vtable order.</summary>
[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, ref Guid eventContext);

    float GetMasterVolume();

    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetMute();
}
