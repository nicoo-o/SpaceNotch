using System;
using System.Runtime.InteropServices;

namespace SpaceNotch.Platform.Windows.Capture;

/// <summary>
/// Temps écoulé depuis le dernier clavier ou la dernière souris de la session
/// (GetLastInputInfo). Une lecture, pas d'écoute : l'écran de veille (P5) la
/// consulte quand il en a besoin.
/// </summary>
public static partial class IdleProbe
{
    public static TimeSpan Idle()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };

        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        // Les deux compteurs bouclent tous les 49 jours : la différence non signée reste juste.
        uint elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LASTINPUTINFO info);
}
