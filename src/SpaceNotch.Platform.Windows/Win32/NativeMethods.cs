using System;
using System.Runtime.InteropServices;

namespace SpaceNotch.Platform.Windows.Win32;

public static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// HIGHCONTRASTW : utilisé par SPI_GETHIGHCONTRAST. cbSize doit être renseigné
    /// avant l'appel, sinon la fonction échoue silencieusement.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HIGHCONTRAST
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    // ---- Boîte de message ------------------------------------------------

    /// <summary>
    /// Boîte de message de Windows : le dernier recours de l'installeur quand
    /// sa propre fenêtre ne peut pas s'ouvrir — mieux qu'un lancement muet.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    // ---- Positionnement / visibilité --------------------------------------

    /// <summary>Dossier connu de l'utilisateur — ici, Téléchargements.</summary>
    [LibraryImport("shell32.dll")]
    public static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // ---- Styles étendus ---------------------------------------------------
    //
    // GetWindowLongPtrW n'est exportée que sur les cibles 64 bits : sur x86 c'est
    // un alias de GetWindowLongW. Les deux exports sont donc déclarés et le choix
    // se fait dans les wrappers ci-dessous.

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static partial int GetWindowLong32(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : (IntPtr)GetWindowLong32(hWnd, nIndex);

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : (IntPtr)SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());

    /// <summary>
    /// Donne le focus clavier à une fenêtre. Utilisé uniquement pendant que
    /// l'utilisateur désigne l'Island : le reste du temps, la fenêtre porte
    /// <c>WS_EX_NOACTIVATE</c> et ne peut pas être activée.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SetFocus(IntPtr hWnd);

    // ---- Écrans -----------------------------------------------------------

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>
    /// Énumère les moniteurs. Déclaré en DllImport (et non LibraryImport) car la
    /// génération de source ne marshale pas directement un délégué de rappel.
    /// </summary>
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    // ---- DPI --------------------------------------------------------------

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetDpiForWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("shcore.dll", SetLastError = true)]
    public static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // ---- Paramètres système ----------------------------------------------

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfoUInt(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfoHighContrast(uint uiAction, uint uiParam, ref HIGHCONTRAST pvParam, uint fWinIni);

    // ---- DWM --------------------------------------------------------------

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
