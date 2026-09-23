using System;
using System.Runtime.InteropServices;

namespace NotchFlow.Platform.Windows.Audio;

public sealed class CoreAudioVolumeListener : IDisposable
{
    private IAudioEndpointVolume? _endpointVolume;
    private AudioEndpointVolumeCallback? _callback;
    private bool _isDisposed;

    /// <summary>Vrai tant que les notifications de volume sont écoutées.</summary>
    public bool IsListening => _endpointVolume is not null;

    public event Action<float, bool>? VolumeChanged;

    public void Start()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);

            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out var endpointObj);
            _endpointVolume = (IAudioEndpointVolume)endpointObj;

            _callback = new AudioEndpointVolumeCallback(this);
            _endpointVolume.RegisterControlChangeNotify(_callback);
        }
        catch
        {
            // Tolérance si aucun périphérique audio n'est disponible
        }
    }

    private void OnNotify(float volume, bool isMuted)
    {
        VolumeChanged?.Invoke(volume * 100.0f, isMuted);
    }

    /// <summary>
    /// Annule l'inscription aux notifications de volume. Appelée par le cycle de
    /// vie de la fonctionnalité, afin qu'une désactivation libère réellement la
    /// ressource au lieu de laisser un abonnement actif.
    /// </summary>
    public void Stop()
    {
        if (_endpointVolume is not null && _callback is not null)
        {
            try
            {
                _endpointVolume.UnregisterControlChangeNotify(_callback);
            }
            catch
            {
                // Tolérance : le périphérique a pu disparaître entre-temps.
            }
        }

        _callback = null;
        _endpointVolume = null;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Stop();
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

    [Guid("5BC64874-38D4-4CE5-801E-50672CD59F96"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        [PreserveSig]
        int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        [PreserveSig]
        int GetChannelCount(out uint pnChannelCount);
        [PreserveSig]
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig]
        int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig]
        int OnNotify(IntPtr pNotify);
    }

    private sealed class AudioEndpointVolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly CoreAudioVolumeListener _listener;

        public AudioEndpointVolumeCallback(CoreAudioVolumeListener listener)
        {
            _listener = listener;
        }

        public int OnNotify(IntPtr pNotify)
        {
            var data = Marshal.PtrToStructure<AUDIO_VOLUME_NOTIFICATION_DATA>(pNotify);
            _listener.OnNotify(data.fMasterVolume, data.bMuted);
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AUDIO_VOLUME_NOTIFICATION_DATA
    {
        public Guid guidEventContext;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bMuted;
        public float fMasterVolume;
        public uint nChannels;
        public float afChannelVolumes;
    }

    private enum EDataFlow { eRender, eCapture, eAll }
    private enum ERole { eConsole, eMultimedia, eCommunications }
    [Flags]
    private enum CLSCTX { CLSCTX_ALL = 23 }
}
