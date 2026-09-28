using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Sablier de pixels (P4) : pendant un téléchargement, chaque pourcent fait
/// tomber un pixel qui s'empile au fond de la notch.
///
/// <para>
/// Le tas monte régulièrement : chaque pixel tombe dans la colonne la plus
/// basse, choisie parmi les ex æquo par un pas premier plutôt qu'au hasard. Le
/// tas a donc toujours la même allure pour la même progression, ce qui se teste
/// et ne scintille pas d'un rendu à l'autre.
/// </para>
/// </summary>
public sealed class PixelHeap
{
    /// <summary>Pas entre deux pixels, en DIP (3 DIP de pixel, 1 d'écart).</summary>
    public const double Pitch = 4;

    /// <summary>Côté d'un pixel, en DIP.</summary>
    public const double PixelSize = 3;

    /// <summary>Durée de la chute d'un pixel, en millisecondes.</summary>
    public const int FallMilliseconds = 420;

    private readonly int[] _heights;
    private readonly List<(int Column, int Row)> _placed = [];

    public PixelHeap(int columns)
    {
        _heights = new int[Math.Max(1, columns)];
    }

    /// <summary>Nombre de colonnes d'un tas qui tient dans <paramref name="width"/> DIP.</summary>
    public static int ColumnsFor(double width) => Math.Max(1, (int)Math.Floor(width / Pitch));

    public int Columns => _heights.Length;

    /// <summary>Nombre de pixels déjà tombés.</summary>
    public int Count => _placed.Count;

    /// <summary>Pixels déjà posés, dans l'ordre de leur chute.</summary>
    public IReadOnlyList<(int Column, int Row)> Placed => _placed;

    /// <summary>Hauteur de la plus haute colonne, en pixels.</summary>
    public int Height
    {
        get
        {
            int max = 0;
            foreach (int h in _heights)
            {
                max = Math.Max(max, h);
            }

            return max;
        }
    }

    /// <summary>Fait tomber un pixel ; renvoie sa colonne et sa rangée (0 = fond).</summary>
    public (int Column, int Row) Drop()
    {
        int min = int.MaxValue;
        foreach (int h in _heights)
        {
            min = Math.Min(min, h);
        }

        int ties = 0;
        foreach (int h in _heights)
        {
            if (h == min)
            {
                ties++;
            }
        }

        int pick = (_placed.Count * 7919) % ties;
        int column = 0;

        for (int i = 0, seen = 0; i < _heights.Length; i++)
        {
            if (_heights[i] != min)
            {
                continue;
            }

            if (seen++ == pick)
            {
                column = i;
                break;
            }
        }

        var cell = (column, _heights[column]++);
        _placed.Add(cell);
        return cell;
    }

    /// <summary>
    /// Fait tomber les pixels qui manquent pour atteindre <paramref name="percent"/>
    /// (0 à 100) et renvoie ceux qui viennent de tomber. Un pourcentage qui recule
    /// — un téléchargement relancé — ne retire rien : le tas ne remonte pas le temps.
    /// </summary>
    public IReadOnlyList<(int Column, int Row)> FillTo(double percent)
    {
        int target = (int)Math.Clamp(Math.Floor(percent), 0, 100);
        var fresh = new List<(int Column, int Row)>();

        while (_placed.Count < target)
        {
            fresh.Add(Drop());
        }

        return fresh;
    }
}
