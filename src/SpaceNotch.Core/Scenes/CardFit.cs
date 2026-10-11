using System;
using SpaceNotch.Core.Activities;

namespace SpaceNotch.Core.Scenes;

/// <summary>
/// Hauteur de la carte générique ajustée à ce qu'elle montre vraiment.
///
/// <para>
/// La carte du répertoire (<see cref="IslandSceneCatalog.Card"/>) réserve la
/// place d'un titre, d'un sous-titre et d'une rangée de contrôles. Une carte
/// d'agent ou de progression n'a souvent ni contrôle ni seconde ligne : sans la
/// trame qui l'habillait, ce qui restait se lisait comme un grand vide noir
/// sous le texte. Une activité qui connaît son contenu déclare donc la hauteur
/// juste, calculée ici à partir des mêmes mesures que la vue.
/// </para>
/// </summary>
public static class CardFit
{
    /// <summary>Largeur du contenu de la carte, celle du répertoire.</summary>
    public const double Width = 312;

    /// <summary>Pastille de la carte (icône, grille, Pixel).</summary>
    public const double Badge = 38;

    /// <summary>Ligne de contexte au-dessus du titre (légende 11, interligne 14).</summary>
    public const double EyebrowLine = 14;

    /// <summary>Titre (13,5 demi-gras).</summary>
    public const double TitleLine = 18;

    /// <summary>Une ligne de sous-titre (11,5).</summary>
    public const double SubtitleLine = 15;

    /// <summary>Fil de progression ou barre à étapes : 3 et sa marge de 4.</summary>
    public const double ProgressRow = 7;

    /// <summary>Écart entre les lignes du texte.</summary>
    public const double TextGap = 3;

    /// <summary>Écart entre les rangées de la carte (en-tête, liste, contrôles).</summary>
    public const double RowGap = 8;

    /// <summary>Une ligne de la liste des dernières actions.</summary>
    public const double RecentLine = 16;

    /// <summary>Écart entre deux lignes de cette liste.</summary>
    public const double RecentGap = 4;

    /// <summary>Contrôles en pastilles (pile, ligne) et au trait (carte).</summary>
    public const double ChipRow = 26;

    public const double ButtonRow = 30;

    /// <summary>Au-delà, un sous-titre passe sur deux lignes (largeur du texte ≈ 262 DIP).</summary>
    public const int SubtitleWrap = 44;

    /// <summary>Hauteur du contenu, sans les marges de la scène.</summary>
    public static double ContentHeight(
        bool eyebrow,
        string? subtitle,
        bool progress,
        bool actions,
        ActivityLayout layout,
        int recentLines = 0)
    {
        double text = TitleLine;

        if (eyebrow)
        {
            text += EyebrowLine + TextGap;
        }

        if (!string.IsNullOrEmpty(subtitle))
        {
            text += TextGap + (subtitle.Length > SubtitleWrap ? 2 * SubtitleLine : SubtitleLine);
        }

        if (progress)
        {
            text += TextGap + ProgressRow;
        }

        double height = Math.Max(layout == ActivityLayout.Row ? 24 : Badge, text);

        if (recentLines > 0)
        {
            height += RowGap + (recentLines * RecentLine) + ((recentLines - 1) * RecentGap);
        }

        if (actions && layout != ActivityLayout.Row)
        {
            height += RowGap + (layout == ActivityLayout.Card ? ButtonRow : ChipRow);
        }

        return Math.Ceiling(height);
    }

    /// <summary>Encombrement ouvert de la carte : son contenu, les marges et les épaules.</summary>
    public static IslandFootprint For(
        bool eyebrow,
        string? subtitle,
        bool progress,
        bool actions,
        ActivityLayout layout,
        int recentLines = 0)
        => SceneInsets.Wrap(Width, ContentHeight(eyebrow, subtitle, progress, actions, layout, recentLines));
}
