using System;
using System.Runtime.InteropServices;

namespace SpaceNotch.Platform.Windows.Audio;

/// <summary>
/// Le micro par défaut (communications) est-il coupé ? Lu à la demande, sans
/// jamais ouvrir le micro : c'est l'état que Windows affiche, pas le son. Sert
/// au miroir avant une réunion (W5).
/// </summary>
public static class MicrophoneState
{
    /// <summary><c>true</c> coupé, <c>false</c> ouvert, <c>null</c> sans micro.</summary>
    public static bool? IsMuted()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out IMMDevice? device) != 0 || device is null)
            {
                return null;
            }

            Guid iid = typeof(IAudioEndpointVolume).GUID;

            if (device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out object endpoint) != 0)
            {
                return null;
            }

            return ((IAudioEndpointVolume)endpoint).GetMute(out bool muted) == 0 ? muted : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotNeeded();

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    private enum EDataFlow { eRender, eCapture, eAll }

    private enum ERole { eConsole, eMultimedia, eCommunications }

    [Flags]
    private enum CLSCTX { CLSCTX_ALL = 23 }
}
