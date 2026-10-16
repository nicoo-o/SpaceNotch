using System;
using SpaceNotch.Core.Presentation;

namespace SpaceNotch.Core.Scenes;

/// <summary>
/// Où se place la notch quand une barre des tâches occupe le haut de l'écran
/// (StartAllBack, ExplorerPatcher). <see cref="Ask"/> tant que l'utilisateur
/// n'a pas répondu : la notch se pose alors sous la barre (ADR-030).
/// </summary>
public enum TopBarPlacement
{
    /// <summary>Pas encore de réponse : la notch le demande, et se pose sous la barre en attendant.</summary>
    Ask,

    /// <summary>Juste sous la barre, avec sa forme et son comportement.</summary>
    UnderBar,

    /// <summary>En languette sur un côté.</summary>
    SideTab
}

/// <summary>
/// Barre des tâches en haut (n° 93, ADR-030). Une barre se lit à la zone de
/// travail de l'écran : elle commence plus bas que l'écran. Une barre en
/// masquage automatique ne réserve rien, et ne compte donc pas — elle glisse
/// par-dessus quand on l'appelle, comme pour toute autre fenêtre.
/// </summary>
public static class TopBar
{
    /// <summary>En dessous, ce n'est pas une barre : une marge, un arrondi de pilote d'écran.</summary>
    public const int MinimumPx = 8;

    /// <summary>Hauteur réservée en haut de l'écran, en pixels physiques ; 0 sans barre.</summary>
    public static int Reserved(int monitorTop, int workTop) => Math.Max(0, workTop - monitorTop);

    /// <summary>Vrai si une barre occupe le haut de l'écran.</summary>
    public static bool IsPresent(int monitorTop, int workTop) => Reserved(monitorTop, workTop) >= MinimumPx;

    /// <summary>
    /// Décalage vertical de la notch accrochée en haut : la hauteur de la barre,
    /// quel que soit le choix — même avec « Sur le côté », une notch ramenée en
    /// haut (raccrochage, menu « Accrocher à ») ne recouvre jamais la barre ; le
    /// choix ne décide que du bord. Sans barre, aucun : la règle n° 1 (le bord
    /// de la notch est le bord de l'écran) reste vraie.
    /// </summary>
    public static int TopInset(int monitorTop, int workTop)
        => IsPresent(monitorTop, workTop) ? Reserved(monitorTop, workTop) : 0;

    /// <summary>Vrai s'il faut demander : une barre est là et l'utilisateur n'a pas encore répondu.</summary>
    public static bool ShouldAsk(int monitorTop, int workTop, TopBarPlacement placement)
        => IsPresent(monitorTop, workTop) && placement == TopBarPlacement.Ask;

    /// <summary>
    /// Le repère d'un bord, en DIP : l'écran, sauf en haut sous une barre, où le
    /// bord est le bas de la barre (n° 96). La goutte d'un détachement et d'un
    /// raccrochage s'y calcule : elle part du bas de la barre, comme la forme
    /// accrochée. Les côtés ne changent pas.
    /// </summary>
    public static ScreenRect FrameBelowBar(NotchEdge edge, ScreenRect screen, double insetDip)
        => edge == NotchEdge.Top && insetDip > 0
            ? new ScreenRect(screen.X, screen.Y + insetDip, screen.Width, Math.Max(0, screen.Height - insetDip))
            : screen;

    /// <summary>
    /// Une pastille qui se raccroche atteint la forme accrochée : la goutte peut
    /// l'aspirer. Mesuré dans le repère du bord, sous la barre s'il y en a une —
    /// mesuré depuis le haut de l'écran, une pastille arrivée sous une barre plus
    /// haute que la forme ne l'atteignait jamais, et le raccrochage ne finissait
    /// pas (relecture de #81).
    /// </summary>
    public static bool PillReachesDock(NotchEdge edge, ScreenRect pill, ScreenRect screen, double insetDip, double attachedDepth)
        => Presentation.EdgeFrame.ToLocal(edge, pill, FrameBelowBar(edge, screen, insetDip)).Y < attachedDepth;

    /// <summary>
    /// La pastille en mouvement, retenue sous la barre : son ressort dépasse sa
    /// cible (raccrochage, retour vers un aimant du haut). Sans barre, ce
    /// dépassement sort de l'écran et ne se voit pas ; sous une barre, il
    /// mordait sur la barre pendant une image (n° 96).
    /// </summary>
    public static ScreenRect KeepBelowBar(NotchEdge edge, ScreenRect pill, ScreenRect screen, double insetDip)
        => edge == NotchEdge.Top && insetDip > 0 && pill.Y < screen.Y + insetDip
            ? new ScreenRect(pill.X, screen.Y + insetDip, pill.Width, pill.Height)
            : pill;

    /// <summary>Vrai si la notch accrochée en haut doit passer en languette : une barre est là et c'est le choix.</summary>
    public static bool WantsSide(int monitorTop, int workTop, TopBarPlacement placement)
        => IsPresent(monitorTop, workTop) && placement == TopBarPlacement.SideTab;
}
