using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>Un pixel de la nouvelle icône et celui de l'ancienne dont il part.</summary>
/// <param name="To">Indice du pixel d'arrivée dans la grille 7 × 7.</param>
/// <param name="From">Indice du pixel de départ.</param>
/// <param name="Order">Rang dans le balayage, de 0 à 1 : fixe le décalage de départ.</param>
public readonly record struct PixelMove(int To, int From, double Order);

/// <summary>
/// Glyphes qui migrent (P6) : quand une icône change, ses pixels voyagent vers
/// la nouvelle forme au lieu de s'éteindre et de se rallumer. C'est la
/// généralisation du spinner qui devient une coche (vague 5a).
///
/// <para>
/// L'appariement suit l'ordre de balayage, colonne par colonne puis de haut en
/// bas : le k-ième pixel allumé de l'arrivée part du pixel de même rang
/// proportionnel au départ. Deux icônes de tailles différentes s'apparient
/// donc sans trou : si l'arrivée compte plus de pixels, certains partent du
/// même point et s'y séparent ; si elle en compte moins, les pixels en trop
/// s'éteignent sur place.
/// </para>
/// </summary>
public static class GlyphMorph
{
    /// <summary>Durée du voyage d'un pixel, en millisecondes.</summary>
    public const int TravelMilliseconds = 380;

    /// <summary>Décalage total du balayage, en millisecondes.</summary>
    public const int StaggerMilliseconds = 160;

    /// <summary>
    /// Appariement d'une icône à la suivante. Vide si l'une des deux n'a aucun
    /// pixel allumé : il n'y a alors rien à faire voyager, l'icône s'allume
    /// comme d'habitude.
    /// </summary>
    public static IReadOnlyList<PixelMove> Pair(IReadOnlyList<bool> from, IReadOnlyList<bool> to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        List<int> sources = Scan(from);
        List<int> targets = Scan(to);

        if (sources.Count == 0 || targets.Count == 0)
        {
            return [];
        }

        var moves = new List<PixelMove>(targets.Count);

        for (int k = 0; k < targets.Count; k++)
        {
            int source = sources[(int)((long)k * sources.Count / targets.Count)];
            double order = targets.Count == 1 ? 0 : (double)k / (targets.Count - 1);
            moves.Add(new PixelMove(targets[k], source, order));
        }

        return moves;
    }

    /// <summary>Décalage d'un pixel dans la grille, en cases : où il commence, par rapport à où il finit.</summary>
    public static (int Dx, int Dy) Offset(PixelMove move, int size = PixelGlyphs.Size)
        => ((move.From % size) - (move.To % size), (move.From / size) - (move.To / size));

    /// <summary>Pixels allumés, colonne par colonne puis de haut en bas.</summary>
    private static List<int> Scan(IReadOnlyList<bool> mask)
    {
        int size = (int)Math.Round(Math.Sqrt(mask.Count));
        var lit = new List<int>();

        for (int column = 0; column < size; column++)
        {
            for (int row = 0; row < size; row++)
            {
                int i = (row * size) + column;

                if (i < mask.Count && mask[i])
                {
                    lit.Add(i);
                }
            }
        }

        return lit;
    }
}
