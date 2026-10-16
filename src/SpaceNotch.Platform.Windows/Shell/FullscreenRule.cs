using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch.Platform.Windows.Shell;

/// <summary>Un rectangle d'écran en pixels physiques, bords exclus à droite et en bas.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom);

/// <summary>Ce qu'une fenêtre de premier niveau dit du plein écran, dans l'ordre d'empilement.</summary>
public enum CoveringWindow
{
    /// <summary>Ne compte pas : elle ne couvre pas l'écran, ou on voit au travers (surcouche, fenêtre masquée).</summary>
    Ignore,

    /// <summary>Opaque et couvrant l'écran sans être un plein écran : ce qui est dessous est caché, on s'arrête.</summary>
    Occludes,

    /// <summary>Un vrai plein écran, devant : la notch se retire.</summary>
    Fullscreen
}

/// <summary>
/// Quand une fenêtre est-elle « le plein écran » devant lequel la notch se retire ?
/// Règle pure, sans appel à Windows, pour être testée (n° 11).
///
/// <para>
/// <c>SHQueryUserNotificationState</c> répond « occupé » (<c>QUNS_BUSY</c>) dès
/// qu'une fenêtre « toujours devant » couvre l'écran — y compris une surcouche
/// transparente que les clics traversent (enregistrement, assistant, mascotte).
/// La notch se retirait alors sans raison visible, et ne revenait pas tant que la
/// surcouche existait. Le shell ne dit pas <em>quelle</em> fenêtre l'occupe : on
/// parcourt les fenêtres de haut en bas, et la première opaque qui couvre l'écran
/// décide.
/// </para>
/// </summary>
public static class FullscreenRule
{
    /// <summary>La fenêtre couvre tout le moniteur, barre des tâches comprise.</summary>
    public static bool Covers(PixelRect window, PixelRect monitor)
        => window.Left <= monitor.Left
            && window.Top <= monitor.Top
            && window.Right >= monitor.Right
            && window.Bottom >= monitor.Bottom;

    /// <summary>La fenêtre dépasse du moniteur d'au moins un côté (le cadre d'une fenêtre agrandie).</summary>
    public static bool Overflows(PixelRect window, PixelRect monitor)
        => window.Left < monitor.Left
            || window.Top < monitor.Top
            || window.Right > monitor.Right
            || window.Bottom > monitor.Bottom;

    /// <summary>
    /// Plein écran : la fenêtre couvre tout le moniteur sans être une fenêtre
    /// agrandie ordinaire. Avec une barre des tâches masquée automatiquement, la
    /// zone de travail est l'écran entier : une fenêtre agrandie le couvre aussi
    /// (mesuré : -11,-11 → 2571,1611, son cadre déborde), et la notch disparaissait
    /// dès qu'on agrandissait une fenêtre (n° 11). Une fenêtre agrandie qui colle
    /// exactement au moniteur, sans cadre — un jeu sans bordure, SDL2 — reste un
    /// plein écran.
    /// </summary>
    public static bool IsFullscreen(PixelRect window, PixelRect monitor, bool maximized)
        => Covers(window, monitor) && !(maximized && Overflows(window, monitor));

    /// <summary>
    /// Une fenêtre visible, rencontrée de haut en bas. <paramref name="inFront"/> :
    /// elle appartient à l'application au premier plan, ou elle est « toujours
    /// devant » — un plein écran laissé en arrière-plan derrière la fenêtre où l'on
    /// travaille ne retire pas la notch.
    /// </summary>
    public static CoveringWindow Classify(
        PixelRect window,
        PixelRect monitor,
        uint extendedStyle,
        bool minimized,
        bool maximized,
        bool cloaked,
        bool shell,
        bool inFront)
    {
        if (!Covers(window, monitor) || minimized || cloaked || shell)
        {
            return CoveringWindow.Ignore;
        }

        // Une surcouche : les clics la traversent, ou elle ne prend jamais le
        // premier plan. On la voit au travers ; ce n'est pas une application
        // qu'on regarde en plein écran.
        if ((extendedStyle & (uint)NativeConstants.WS_EX_TRANSPARENT) != 0
            || (extendedStyle & (uint)NativeConstants.WS_EX_NOACTIVATE) != 0)
        {
            return CoveringWindow.Ignore;
        }

        return IsFullscreen(window, monitor, maximized) && inFront
            ? CoveringWindow.Fullscreen
            : CoveringWindow.Occludes;
    }
}
