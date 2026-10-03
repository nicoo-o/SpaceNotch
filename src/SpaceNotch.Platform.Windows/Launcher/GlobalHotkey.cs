using System;
using System.Collections.Generic;
using System.Linq;
using SpaceNotch.Core.Localization;
using System.Runtime.InteropServices;

namespace SpaceNotch.Platform.Windows.Launcher;

/// <summary>
/// Le raccourci global qui ouvre la recherche depuis n'importe où. Celui que
/// l'utilisateur a choisi d'abord, puis les autres dans l'ordre de
/// <see cref="Choices"/> : Alt+Espace est souvent pris (PowerToys, palette de
/// commandes) et Win+Maj+Espace l'est par Windows (langue de saisie) — sans un
/// troisième choix, la recherche n'avait plus de raccourci du tout. Le message
/// WM_HOTKEY arrive à la fenêtre qui l'a enregistré.
/// </summary>
public static partial class GlobalHotkey
{
    public const int WmHotkey = 0x0312;
    public const int LauncherId = 0x5343; // « SC »

    /// <summary>Raccourci « aller à la notch » (phase C) : le clavier et le focus vont à la notch.</summary>
    public const int FocusId = 0x534E; // « SN »

    /// <summary>Clé du réglage qui laisse SpaceNotch prendre le premier raccourci libre.</summary>
    public const string Auto = "auto";

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkN = 0x4E;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkSpace = 0x20;

    /// <summary>Un raccourci proposé : sa clé de réglage, ses modificateurs et son libellé.</summary>
    public sealed record Choice(string Key, uint Modifiers, string French, string English)
    {
        public string Label => Lang.T(French, English);
    }

    /// <summary>Les raccourcis proposés, dans l'ordre d'essai en mode automatique.</summary>
    public static IReadOnlyList<Choice> Choices { get; } =
    [
        new("alt-space", ModAlt, "Alt+Espace", "Alt+Space"),
        new("ctrl-alt-space", ModControl | ModAlt, "Ctrl+Alt+Espace", "Ctrl+Alt+Space"),
        new("win-shift-space", ModWin | ModShift, "Win+Maj+Espace", "Win+Shift+Space"),
        new("ctrl-shift-space", ModControl | ModShift, "Ctrl+Maj+Espace", "Ctrl+Shift+Space")
    ];

    /// <summary>Vrai si <paramref name="key"/> est <see cref="Auto"/> ou l'un des <see cref="Choices"/>.</summary>
    public static bool IsKnown(string? key) => key == Auto || Choices.Any(c => c.Key == key);

    /// <summary>
    /// Enregistre le raccourci de la recherche, en remplaçant le précédent.
    /// Renvoie celui qui a été retenu, pour l'afficher, ou <c>null</c> si tous
    /// étaient pris.
    /// </summary>
    /// <param name="window">Fenêtre qui recevra WM_HOTKEY.</param>
    /// <param name="preferred">Clé du raccourci voulu, essayé en premier ; <see cref="Auto"/> ou <c>null</c> : l'ordre de <see cref="Choices"/>.</param>
    public static string? RegisterLauncher(IntPtr window, string? preferred = null)
    {
        UnregisterHotKey(window, LauncherId);

        IEnumerable<Choice> order = Choices
            .OrderBy(c => c.Key == preferred ? 0 : 1);

        foreach (Choice choice in order)
        {
            if (RegisterHotKey(window, LauncherId, choice.Modifiers | ModNoRepeat, VkSpace))
            {
                return choice.Label;
            }
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
