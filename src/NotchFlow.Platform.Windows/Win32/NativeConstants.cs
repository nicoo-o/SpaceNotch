using System;

namespace NotchFlow.Platform.Windows.Win32;

/// <summary>
/// Constantes Win32 utilisées par NotchFlow. Les noms suivent volontairement la
/// convention des en-têtes Windows (majuscules, traits de soulignement) afin de
/// rester vérifiables face à la documentation officielle.
/// </summary>
public static class NativeConstants
{
    // ---- Fenêtres : insertion Z / positionnement -------------------------

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;

    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    // ---- Écrans -----------------------------------------------------------

    public const int MONITOR_DEFAULTTONEAREST = 2;
    public const int MONITOR_DEFAULTTOPRIMARY = 1;

    /// <summary>MONITORINFOF_PRIMARY</summary>
    public const uint MONITORINFOF_PRIMARY = 0x00000001;

    public const int MDT_EFFECTIVE_DPI = 0;
    public const int MDT_ANGULAR_DPI = 1;
    public const int MDT_RAW_DPI = 2;

    /// <summary>DPI de référence pour une mise à l'échelle de 100 %.</summary>
    public const uint DEFAULT_DPI = 96;

    // ---- DWM --------------------------------------------------------------

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_CAPTION_COLOR = 35;
    public const int DWMWA_TEXT_COLOR = 36;

    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    // ---- Messages ---------------------------------------------------------

    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_THEMECHANGED = 0x031A;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_DWMCOMPOSITIONCHANGED = 0x031E;

    /// <summary>Diffusé aux fenêtres inscrites via AddClipboardFormatListener.</summary>
    public const uint WM_CLIPBOARDUPDATE = 0x031D;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONUP = 0x0205;

    // ---- SPI_* (SystemParametersInfo) -------------------------------------

    public const uint SPI_GETUIEFFECTS = 0x103E;
    public const uint SPI_SETUIEFFECTS = 0x103F;
    public const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
    public const uint SPI_GETHIGHCONTRAST = 0x0042;
    public const uint SPI_SETHIGHCONTRAST = 0x0043;
    public const uint SPI_SETWORKAREA = 0x002F;
    public const uint SPI_SETNONCLIENTMETRICS = 0x002A;
    public const uint SPI_GETSCREENREADER = 0x0046;

    public const uint HCF_HIGHCONTRASTON = 0x00000001;

    // ---- Résultats de hit-test -------------------------------------------

    public const nint HTTRANSPARENT = -1;
    public const nint HTNOWHERE = 0;
    public const nint HTCLIENT = 1;
    public const nint HTCAPTION = 2;
}
