using System;
using NotchFlow.Platform.Windows.Win32;

namespace NotchFlow.Platform.Windows.Display;

/// <summary>
/// Lecture du DPI réel d'une fenêtre ou d'un moniteur.
///
/// Le cahier des charges impose une exactitude au pixel de 100 % à 250 %. Cela
/// suppose de convertir les DIPs avec l'échelle du moniteur *cible*, et non avec
/// celle de la fenêtre courante — deux moniteurs peuvent avoir des échelles
/// différentes dans le même bureau virtuel.
/// </summary>
public static class MonitorDpi
{
    /// <summary>DPI correspondant à une mise à l'échelle de 100 %.</summary>
    public const uint DefaultDpi = NativeConstants.DEFAULT_DPI;

    /// <summary>
    /// DPI du moniteur qui héberge actuellement la fenêtre. C'est l'appel
    /// recommandé par Microsoft lorsque l'on dispose d'un HWND.
    /// </summary>
    public static uint GetForWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return DefaultDpi;
        }

        try
        {
            uint dpi = NativeMethods.GetDpiForWindow(hWnd);
            return dpi == 0 ? DefaultDpi : dpi;
        }
        catch (EntryPointNotFoundException)
        {
            // Système trop ancien pour GetDpiForWindow : on retombe sur 100 %.
            return DefaultDpi;
        }
        catch (DllNotFoundException)
        {
            return DefaultDpi;
        }
    }

    /// <summary>
    /// DPI effectif d'un moniteur donné, y compris lorsque ce n'est pas celui de
    /// la fenêtre. Retourne 96 si l'appel n'est pas disponible.
    /// </summary>
    public static uint GetForMonitor(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero)
        {
            return DefaultDpi;
        }

        try
        {
            int hr = NativeMethods.GetDpiForMonitor(
                hMonitor,
                NativeConstants.MDT_EFFECTIVE_DPI,
                out uint dpiX,
                out _);

            return hr == 0 && dpiX != 0 ? dpiX : DefaultDpi;
        }
        catch (EntryPointNotFoundException)
        {
            return DefaultDpi;
        }
        catch (DllNotFoundException)
        {
            return DefaultDpi;
        }
    }

    /// <summary>Transforme un DPI en facteur d'échelle (96 → 1.0, 144 → 1.5).</summary>
    public static double ToScale(uint dpi)
        => dpi == 0 ? 1.0 : dpi / (double)DefaultDpi;

    /// <summary>DIPs vers pixels physiques, arrondi au plus proche.</summary>
    public static int ToPhysicalPixels(double dips, double scale)
        => (int)Math.Round(dips * (scale <= 0 ? 1.0 : scale), MidpointRounding.AwayFromZero);

    /// <summary>Pixels physiques vers DIPs.</summary>
    public static double ToDips(int physicalPixels, double scale)
        => physicalPixels / (scale <= 0 ? 1.0 : scale);
}
