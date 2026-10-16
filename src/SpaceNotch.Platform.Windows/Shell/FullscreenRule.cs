namespace SpaceNotch.Platform.Windows.Shell;

/// <summary>Un rectangle d'écran en pixels physiques, bords exclus à droite et en bas.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom);

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
/// la cherche, et une surcouche traversable ne compte pas.
/// </para>
/// </summary>
public static class FullscreenRule
{
    /// <summary><c>WS_EX_TRANSPARENT</c> : les clics traversent la fenêtre.</summary>
    public const uint ExTransparent = 0x00000020;

    /// <summary>La fenêtre couvre tout le moniteur, barre des tâches comprise.</summary>
    public static bool Covers(PixelRect window, PixelRect monitor)
        => window.Left <= monitor.Left
            && window.Top <= monitor.Top
            && window.Right >= monitor.Right
            && window.Bottom >= monitor.Bottom;

    /// <summary>
    /// Plein écran : la fenêtre couvre tout le moniteur sans être simplement
    /// agrandie. Avec une barre des tâches masquée automatiquement, la zone de
    /// travail est l'écran entier : une fenêtre agrandie le couvre aussi, et la
    /// notch disparaissait dès qu'on agrandissait une fenêtre (n° 11). Une vidéo,
    /// un jeu sans bordure, un navigateur en F11 ne sont pas « agrandis ».
    /// </summary>
    public static bool IsFullscreen(PixelRect window, PixelRect monitor, bool maximized)
        => !maximized && Covers(window, monitor);

    /// <summary>
    /// Une fenêtre peut-elle être le plein écran que le shell signale : visible,
    /// ni réduite ni masquée (autre bureau virtuel), ni le bureau, ni SpaceNotch,
    /// pas traversable par les clics, et plein écran sur le moniteur de la notch.
    /// </summary>
    public static bool IsFullscreenWindow(
        PixelRect window,
        PixelRect monitor,
        uint extendedStyle,
        bool visible,
        bool minimized,
        bool maximized,
        bool cloaked,
        bool shell,
        bool ours)
        => visible
            && !minimized
            && !cloaked
            && !shell
            && !ours
            && (extendedStyle & ExTransparent) == 0
            && IsFullscreen(window, monitor, maximized);
}
