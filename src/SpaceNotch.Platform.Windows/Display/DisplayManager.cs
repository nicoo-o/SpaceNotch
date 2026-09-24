using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch.Platform.Windows.Display;

/// <summary>
/// Comment l'Island choisit son moniteur d'ancrage.
/// </summary>
public enum IslandDisplayTarget
{
    /// <summary>Toujours sur l'écran principal.</summary>
    Primary,

    /// <summary>Sur l'écran qui contient le pointeur au moment de l'affichage.</summary>
    Current,

    /// <summary>Sur un moniteur explicitement désigné par sa clé.</summary>
    Custom
}

/// <summary>
/// Gestionnaire d'écrans : énumération complète, DPI réel par moniteur et calcul
/// du placement de l'Island au sommet, centré au pixel près.
/// </summary>
public static class DisplayManager
{
    /// <summary>
    /// Informations du moniteur qui héberge la fenêtre, ou du moniteur principal
    /// si aucun HWND n'est exploitable.
    /// </summary>
    public static DisplayInfo GetDisplayForWindow(IntPtr hWnd)
    {
        IntPtr hMonitor = hWnd == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.MonitorFromWindow(hWnd, NativeConstants.MONITOR_DEFAULTTONEAREST);

        return hMonitor == IntPtr.Zero ? GetPrimaryDisplay() : FromMonitor(hMonitor);
    }

    /// <summary>
    /// Moniteur contenant un point de l'espace du bureau virtuel. Sert à
    /// implémenter le mode « écran courant » sans scrutation.
    /// </summary>
    public static DisplayInfo GetDisplayForPoint(int x, int y)
    {
        var point = new NativeMethods.POINT { X = x, Y = y };
        IntPtr hMonitor = NativeMethods.MonitorFromPoint(point, NativeConstants.MONITOR_DEFAULTTONEAREST);

        return hMonitor == IntPtr.Zero ? GetPrimaryDisplay() : FromMonitor(hMonitor);
    }

    /// <summary>Moniteur principal, ou repli 1920×1080 si l'API échoue.</summary>
    public static DisplayInfo GetPrimaryDisplay()
    {
        IntPtr hMonitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.POINT { X = 0, Y = 0 },
            NativeConstants.MONITOR_DEFAULTTOPRIMARY);

        return hMonitor == IntPtr.Zero ? FallbackDisplay() : FromMonitor(hMonitor);
    }

    /// <summary>
    /// Tous les moniteurs connectés. Aucune scrutation : à appeler uniquement
    /// lors d'un démarrage ou après un message de changement d'affichage.
    /// </summary>
    public static IReadOnlyList<DisplayInfo> GetAllDisplays()
    {
        var results = new List<DisplayInfo>();

        bool callback(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data)
        {
            results.Add(FromMonitor(hMonitor));
            return true;
        }

        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            return [GetPrimaryDisplay()];
        }

        if (results.Count == 0)
        {
            results.Add(GetPrimaryDisplay());
        }

        return results;
    }

    /// <summary>
    /// Résout le moniteur cible selon la préférence utilisateur. Le mode
    /// <see cref="IslandDisplayTarget.Custom"/> retombe sur l'écran principal si
    /// l'identifiant fourni n'existe plus (moniteur débranché).
    /// </summary>
    public static DisplayInfo ResolveTarget(
        IslandDisplayTarget target,
        IntPtr customHandle = default)
        => target switch
        {
            IslandDisplayTarget.Current => ResolveFromCursor(),
            IslandDisplayTarget.Custom => customHandle == IntPtr.Zero ? GetPrimaryDisplay() : FromMonitor(customHandle),
            _ => GetPrimaryDisplay()
        };

    /// <summary>Moniteur sous le curseur, sans scrutation (lecture ponctuelle).</summary>
    public static DisplayInfo ResolveFromCursor()
    {
        if (!NativeMethods.GetCursorPos(out var pos))
        {
            return GetPrimaryDisplay();
        }

        return GetDisplayForPoint(pos.X, pos.Y);
    }

    /// <summary>
    /// Position physique du coin supérieur gauche de l'Island : collée au sommet
    /// du moniteur et centrée horizontalement.
    /// </summary>
    public static (int X, int Y) CalculateTopCenteredPosition(
        DisplayInfo display,
        int physicalWidth,
        int topOffsetPhysical = 0)
    {
        int x = display.Left + ((display.Width - physicalWidth) / 2);
        int y = display.Top + topOffsetPhysical;

        return (x, y);
    }

    /// <summary>
    /// Construit un <see cref="DisplayInfo"/> depuis un handle de moniteur, en
    /// lisant le DPI réel de ce moniteur précis.
    /// </summary>
    public static DisplayInfo FromMonitor(IntPtr hMonitor)
    {
        var mi = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (hMonitor == IntPtr.Zero || !NativeMethods.GetMonitorInfoW(hMonitor, ref mi))
        {
            return FallbackDisplay();
        }

        return new DisplayInfo(
            Handle: hMonitor,
            Left: mi.rcMonitor.Left,
            Top: mi.rcMonitor.Top,
            Right: mi.rcMonitor.Right,
            Bottom: mi.rcMonitor.Bottom,
            WorkLeft: mi.rcWork.Left,
            WorkTop: mi.rcWork.Top,
            WorkRight: mi.rcWork.Right,
            WorkBottom: mi.rcWork.Bottom,
            IsPrimary: (mi.dwFlags & NativeConstants.MONITORINFOF_PRIMARY) != 0,
            Dpi: MonitorDpi.GetForMonitor(hMonitor));
    }

    private static DisplayInfo FallbackDisplay() => new(
        Handle: IntPtr.Zero,
        Left: 0,
        Top: 0,
        Right: 1920,
        Bottom: 1080,
        WorkLeft: 0,
        WorkTop: 0,
        WorkRight: 1920,
        WorkBottom: 1040,
        IsPrimary: true,
        Dpi: MonitorDpi.DefaultDpi);
}
