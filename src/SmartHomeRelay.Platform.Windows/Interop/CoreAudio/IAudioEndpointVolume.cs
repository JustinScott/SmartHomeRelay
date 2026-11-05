using System.Runtime.InteropServices;

namespace SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

/// <summary><c>IAudioEndpointVolume</c> (endpointvolume.h). Every method is declared, in vtable order.</summary>
[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    int GetChannelCount();

    void SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

    void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

    float GetMasterVolumeLevel();

    float GetMasterVolumeLevelScalar();

    void SetChannelVolumeLevel(int channel, float levelDb, ref Guid eventContext);

    void SetChannelVolumeLevelScalar(int channel, float level, ref Guid eventContext);

    float GetChannelVolumeLevel(int channel);

    float GetChannelVolumeLevelScalar(int channel);

    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetMute();

    void GetVolumeStepInfo(out int step, out int stepCount);

    void VolumeStepUp(ref Guid eventContext);

    void VolumeStepDown(ref Guid eventContext);

    int QueryHardwareSupport();

    void GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}
