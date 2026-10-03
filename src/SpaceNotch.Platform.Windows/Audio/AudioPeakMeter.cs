using System;
using System.Runtime.InteropServices;

namespace SpaceNotch.Platform.Windows.Audio;

/// <summary>
/// Niveau crête de la sortie audio par défaut, entre 0 et 1 : ce que Windows
/// affiche dans le mélangeur de volume. Il sert à faire suivre la musique à la
/// trame de la notch, sans jamais enregistrer ni analyser le son lui-même.
///
/// <para>
/// La sortie par défaut peut changer (un casque qu'on branche) : la mesure se
/// relie toute seule à la nouvelle sortie dès qu'une lecture échoue, et au plus
/// toutes les cinq secondes.
/// </para>
/// </summary>
public sealed class AudioPeakMeter : IDisposable
{
    private static AudioPeakMeter? _shared;

    /// <summary>
    /// Le crête-mètre partagé (phase D) : l'égaliseur de la pastille et la
    /// danse de Pixel lisaient chacun le leur, soit deux points d'accès au
    /// périphérique audio pour la même valeur. Libéré par <see cref="DisposeShared"/>.
    /// </summary>
    public static AudioPeakMeter Shared => _shared ??= new AudioPeakMeter();

    /// <summary>Libère le crête-mètre partagé (à l'arrêt de l'application).</summary>
    public static void DisposeShared()
    {
        _shared?.Dispose();
        _shared = null;
    }

    private static readonly TimeSpan RebindEvery = TimeSpan.FromSeconds(5);

    private IMMDeviceEnumerator? _enumerator;
    private IAudioMeterInformation? _meter;
    private DateTime _boundAt;
    private bool _isDisposed;

    /// <summary>Niveau crête actuel, 0 si aucune sortie n'est disponible.</summary>
    public float Read()
    {
        if (_isDisposed)
        {
            return 0;
        }

        try
        {
            if (_meter is null || DateTime.UtcNow - _boundAt > RebindEvery)
            {
                Bind();
            }

            if (_meter is not null && _meter.GetPeakValue(out float peak) == 0)
            {
                return Math.Clamp(peak, 0f, 1f);
            }
        }
        catch (COMException)
        {
            // Sortie retirée pendant la lecture : on se reliera au prochain appel.
        }
        catch (InvalidCastException)
        {
        }

        Release();
        return 0;
    }

    private void Bind()
    {
        Release();
        _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _boundAt = DateTime.UtcNow;

        if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice? device) != 0 || device is null)
        {
            return;
        }

        var iid = typeof(IAudioMeterInformation).GUID;

        if (device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out object meter) == 0)
        {
            _meter = (IAudioMeterInformation)meter;
        }
    }

    private void Release()
    {
        if (_meter is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_meter);
            }
            catch (ArgumentException)
            {
            }

            _meter = null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Release();

        if (_enumerator is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_enumerator);
            }
            catch (ArgumentException)
            {
            }

            _enumerator = null;
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

    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float pfPeak);
    }

    private enum EDataFlow { eRender, eCapture, eAll }

    private enum ERole { eConsole, eMultimedia, eCommunications }

    [Flags]
    private enum CLSCTX { CLSCTX_ALL = 23 }
}
