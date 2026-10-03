using System;
using SpaceNotch.Core.Localization;
using System.Runtime.InteropServices;

namespace SpaceNotch.Platform.Windows.Launcher;

/// <summary>
/// Le raccourci global qui ouvre la recherche depuis n'importe où. Alt+Espace
/// d'abord ; si une autre application l'a déjà pris, Win+Maj+Espace. Le
/// message WM_HOTKEY arrive à la fenêtre qui l'a enregistré.
/// </summary>
public static partial class GlobalHotkey
{
    public const int WmHotkey = 0x0312;
    public const int LauncherId = 0x5343; // « SC »

    /// <summary>Raccourci « aller à la notch » (phase C) : le clavier et le focus vont à la notch.</summary>
    public const int FocusId = 0x534E; // « SN »

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkN = 0x4E;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkSpace = 0x20;

    /// <summary>
    /// Enregistre le raccourci de la recherche. Renvoie celui qui a été retenu,
    /// pour l'afficher, ou <c>null</c> si les deux étaient pris.
    /// </summary>
    public static string? RegisterLauncher(IntPtr window)
    {
        if (RegisterHotKey(window, LauncherId, ModAlt | ModNoRepeat, VkSpace))
        {
            return Lang.T("Alt+Espace", "Alt+Space");
        }

        if (RegisterHotKey(window, LauncherId, ModWin | ModShift | ModNoRepeat, VkSpace))
        {
            return Lang.T("Win+Maj+Espace", "Win+Shift+Space");
        }

        return null;
    }

    /// <summary>
    /// Enregistre « aller à la notch » : Win+Alt+N, sinon Ctrl+Alt+N. Renvoie
    /// celui qui a été retenu, ou <c>null</c>.
    /// </summary>
    public static string? RegisterFocus(IntPtr window)
    {
        if (RegisterHotKey(window, FocusId, ModWin | ModAlt | ModNoRepeat, VkN))
        {
            return "Win+Alt+N";
        }

        if (RegisterHotKey(window, FocusId, ModControl | ModAlt | ModNoRepeat, VkN))
        {
            return "Ctrl+Alt+N";
        }

        return null;
    }

    public static void Unregister(IntPtr window)
    {
        UnregisterHotKey(window, LauncherId);
        UnregisterHotKey(window, FocusId);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr window, int id);
}
