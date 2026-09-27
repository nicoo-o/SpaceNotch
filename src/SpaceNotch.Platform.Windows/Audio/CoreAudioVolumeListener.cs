using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SpaceNotch.Platform.Windows.Audio;

/// <summary>
/// Volume de la sortie audio par défaut, et la sortie elle-même.
///
/// <para>
/// L'écouteur se liait une fois à la sortie par défaut du démarrage : brancher
/// un casque Bluetooth, et la notch continuait d'afficher — et de suivre — le
/// volume des haut-parleurs. Il s'abonne maintenant aux changements de sortie
/// de Windows (<c>IMMNotificationClient</c>), se relie à la nouvelle, et donne
/// son nom (« AirPods Pro », « Haut-parleurs ») à l'indicateur.
/// </para>
/// </summary>
public sealed class CoreAudioVolumeListener : IDisposable
{
    private static readonly PropertyKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    private readonly Lock _gate = new();

    private IMMDeviceEnumerator? _enumerator;
    private DeviceNotifications? _notifications;
    private IAudioEndpointVolume? _endpointVolume;
    private AudioEndpointVolumeCallback? _callback;
    private bool _isDisposed;

    /// <summary>Vrai tant que les notifications de volume sont écoutées.</summary>
    public bool IsListening => _endpointVolume is not null;

    /// <summary>Nom de la sortie audio actuelle, ou <c>null</c> s'il est inconnu.</summary>
    public string? DeviceName { get; private set; }

    /// <summary>Volume (0 à 100) et muet. Levé aussi quand la sortie change.</summary>
    public event Action<float, bool>? VolumeChanged;

    public void Start()
    {
        lock (_gate)
        {
            try
            {
                _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();

                if (_notifications is null)
                {
                    _notifications = new DeviceNotifications(this);
                    _enumerator.RegisterEndpointNotificationCallback(_notifications);
                }

                BindDefault();
            }
            catch
            {
                // Tolérance si aucun périphérique audio n'est disponible.
            }
        }
    }

    /// <summary>Se lie à la sortie par défaut du moment : son volume et son nom.</summary>
    private void BindDefault()
    {
        Unbind();

        if (_enumerator is null
            || _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice? device) != 0
            || device is null)
        {
            DeviceName = null;
            return;
        }

        DeviceName = ReadName(device);

        var iid = typeof(IAudioEndpointVolume).GUID;

        if (device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out object endpointObj) != 0)
        {
            return;
        }

        _endpointVolume = (IAudioEndpointVolume)endpointObj;
        _callback = new AudioEndpointVolumeCallback(this);
        _endpointVolume.RegisterControlChangeNotify(_callback);
    }

    private void Unbind()
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

    /// <summary>
    /// Règle le volume de la sortie par défaut (0..1), et le démute s'il monte.
    /// Sert à la molette sur la notch et au fader cranté. Faux si aucune sortie.
    /// </summary>
    public bool SetLevel(float level)
    {
        lock (_gate)
        {
            if (_endpointVolume is null)
            {
                return false;
            }

            Guid context = Guid.Empty;
            float clamped = Math.Clamp(level, 0f, 1f);

            if (_endpointVolume.SetMasterVolumeLevelScalar(clamped, ref context) != 0)
            {
                return false;
            }

            if (clamped > 0)
            {
                _endpointVolume.SetMute(false, ref context);
            }

            return true;
        }
    }

    /// <summary>Volume actuel (0..1), ou <c>null</c> sans sortie.</summary>
    public float? Level
    {
        get
        {
            lock (_gate)
            {
                return _endpointVolume is not null && _endpointVolume.GetMasterVolumeLevelScalar(out float level) == 0 ? level : null;
            }
        }
    }

    /// <summary>
    /// La sortie par défaut a changé. On ne se relie pas depuis le rappel lui-même
    /// — Windows le déconseille, l'appel se fait sur son propre fil — mais juste
    /// après, puis on annonce le volume de la nouvelle sortie.
    /// </summary>
    private void OnDefaultChanged()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            float volume;
            bool muted;

            lock (_gate)
            {
                if (_isDisposed || _enumerator is null)
                {
                    return;
                }

                try
                {
                    BindDefault();
                }
                catch
                {
                    return;
                }

                if (_endpointVolume is null
                    || _endpointVolume.GetMasterVolumeLevelScalar(out volume) != 0
                    || _endpointVolume.GetMute(out muted) != 0)
                {
                    return;
                }
            }

            VolumeChanged?.Invoke(volume * 100.0f, muted);
        });
    }

    private static string? ReadName(IMMDevice device)
    {
        if (device.OpenPropertyStore(0 /* STGM_READ */, out IPropertyStore? store) != 0 || store is null)
        {
            return null;
        }

        PropertyKey key = FriendlyName;
        var value = default(PropVariant);

        try
        {
            if (store.GetValue(ref key, out value) != 0 || value.Type != 31 /* VT_LPWSTR */)
            {
                return null;
            }

            return Marshal.PtrToStringUni(value.Pointer);
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    private void OnNotify(float volume, bool isMuted)
    {
        VolumeChanged?.Invoke(volume * 100.0f, isMuted);
    }

    /// <summary>
    /// Annule l'inscription aux notifications de volume et de sortie. Appelée par
    /// le cycle de vie de la fonctionnalité, afin qu'une désactivation libère
    /// réellement la ressource au lieu de laisser un abonnement actif.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            Unbind();

            if (_enumerator is not null && _notifications is not null)
            {
                try
                {
                    _enumerator.UnregisterEndpointNotificationCallback(_notifications);
                }
                catch
                {
                    // Tolérance : le service audio a pu s'arrêter.
                }
            }

            _notifications = null;
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Stop();
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotNeeded();

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);

        int GetDeviceNotNeeded();

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IMMNotificationClient client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

        [PreserveSig]
        int OpenPropertyStore(int access, out IPropertyStore properties);
    }

    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig]
        int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);

        [PreserveSig]
        int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

        [PreserveSig]
        int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
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
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig]
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
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

    /// <summary>Changements de sortie : seul le changement de sortie par défaut (multimédia) compte.</summary>
    private sealed class DeviceNotifications(CoreAudioVolumeListener listener) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, uint newState) => 0;

        public int OnDeviceAdded(string deviceId) => 0;

        public int OnDeviceRemoved(string deviceId) => 0;

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (flow == EDataFlow.eRender && role == ERole.eMultimedia)
            {
                listener.OnDefaultChanged();
            }

            return 0;
        }

        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, int id)
    {
        public Guid Format = format;
        public int Id = id;
    }

    /// <summary>PROPVARIANT réduit à ce qu'on lit : le type, puis un pointeur (VT_LPWSTR).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public IntPtr Pointer;
    }

    private enum EDataFlow { eRender, eCapture, eAll }
    private enum ERole { eConsole, eMultimedia, eCommunications }
    [Flags]
    private enum CLSCTX { CLSCTX_ALL = 23 }
}
