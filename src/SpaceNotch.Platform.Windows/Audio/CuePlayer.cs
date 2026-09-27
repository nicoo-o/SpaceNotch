using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpaceNotch.Core.Sound;

namespace SpaceNotch.Platform.Windows.Audio;

/// <summary>
/// Joue les sons discrets (D3) par <c>PlaySound(SND_MEMORY | SND_ASYNC)</c> :
/// aucun fichier, aucune bibliothèque audio. Le son passe par la session de
/// l'application et suit donc le volume de Windows. Les trois WAV sont
/// synthétisés une fois et épinglés en mémoire, car la lecture asynchrone les
/// lit après le retour de l'appel.
/// </summary>
public static class CuePlayer
{
    private static readonly Dictionary<SoundCueKind, GCHandle> Pinned = [];
    private static readonly object Gate = new();

    public static void Play(SoundCueKind kind)
    {
        try
        {
            IntPtr data;

            lock (Gate)
            {
                if (!Pinned.TryGetValue(kind, out GCHandle handle))
                {
                    handle = GCHandle.Alloc(SoundCue.Wav(kind), GCHandleType.Pinned);
                    Pinned[kind] = handle;
                }

                data = handle.AddrOfPinnedObject();
            }

            PlaySound(data, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault | SndNoStop);
        }
        catch (Exception)
        {
            // Pas de sortie audio : le silence, simplement.
        }
    }

    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;
    private const uint SndNoStop = 0x0010;

    [DllImport("winmm.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
}
