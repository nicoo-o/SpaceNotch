using System;
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

    private const uint ModAlt = 0x0001;
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
            return "Alt+Espace";
        }

        if (RegisterHotKey(window, LauncherId, ModWin | ModShift | ModNoRepeat, VkSpace))
        {
            return "Win+Maj+Espace";
        }

        return null;
    }

    public static void Unregister(IntPtr window) => UnregisterHotKey(window, LauncherId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr window, int id);
}
