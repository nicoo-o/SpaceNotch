using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>Zone à laisser noire, en pixels physiques : un texte, une image, un contrôle.</summary>
public readonly record struct TrameRect(double Left, double Top, double Right, double Bottom)
{
    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

    public TrameRect Inflate(double by) => new(Left - by, Top - by, Right + by, Bottom + by);
}

/// <summary>
/// La trame des scènes ouvertes (choix B3 de la vague 5) : une trame ordonnée
/// de pixels de 3 DIP, dans la couleur de l'activité, qui monte du bas de la
/// notch, plus dense au centre, et s'efface avant d'atteindre le contenu.
///
/// <para>
/// Elle se voit sur un écran OLED parce que ce sont de vrais pixels allumés,
/// là où un grain resterait noir. Elle ne passe jamais derrière le contenu :
/// chaque texte, image ou contrôle garde une marge noire. Dans le lecteur, elle
/// monte et descend avec le niveau de la musique.
/// </para>
/// </summary>
public static class TrameField
{
    /// <summary>Côté d'un pixel de trame, en DIPs.</summary>
    public const double CellDip = 3;

    /// <summary>Marge noire autour du contenu, en DIPs.</summary>
    public const double ClearanceDip = 6;

    /// <summary>Hauteur minimale d'une scène pour porter une trame, en DIPs : jamais sur une pastille.</summary>
    public const double MinimumHeightDip = 70;

    /// <summary>Opacité des pixels de trame.</summary>
    public const double Opacity = 0.6;

    private static readonly int[] Bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    /// <summary>
    /// Densité voulue en un point (0 = noir, 1 = plein), avant le seuil ordonné.
    /// Nulle dans les 40 % du haut, croissante vers le bas, plus forte au centre.
    /// </summary>
    /// <param name="level">Poussée de la musique, 0..1 ; <c>null</c> hors musique.</param>
    public static double Density(double x, double y, double width, double height, double? level = null)
    {
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        double rise = Math.Max(0, ((y / height) - 0.4) / 0.6);
        double centre = 1 - Math.Pow(Math.Abs((x / width) - 0.5) * 2, 3);
        double push = level is { } l ? 0.55 + (Math.Clamp(l, 0, 1) * 0.75) : 1;

        return rise * rise * 0.46 * Math.Max(0, centre) * push;
    }

    /// <summary>
    /// Cellules allumées, en coordonnées de cellule (colonne, rangée), pour une
    /// scène de <paramref name="width"/> × <paramref name="height"/> pixels
    /// physiques. Les cellules hors de la silhouette (congés du bas, épaules) et
    /// celles qui touchent le contenu restent éteintes.
    /// </summary>
    public static List<(int Column, int Row)> Cells(
        double width,
        double height,
        double cell,
        double bottomRadius,
        double shoulder,
        IReadOnlyList<TrameRect> avoid,
        double? level = null)
    {
        ArgumentNullException.ThrowIfNull(avoid);
        var lit = new List<(int, int)>();

        if (cell < 1 || width <= 0 || height <= 0)
        {
            return lit;
        }

        int columns = (int)(width / cell), rows = (int)(height / cell);
        double bodyLeft = shoulder, bodyWidth = width - (2 * shoulder);

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                double x = (column + 0.5) * cell, y = (row + 0.5) * cell;
                double density = Density(x - bodyLeft, y, bodyWidth, height, level);

                if (density * 16 <= Bayer[((row % 4) * 4) + (column % 4)] + 0.5)
                {
                    continue;
                }

                if (!Inside(x, y, width, height, bottomRadius, shoulder, cell) || Touches(avoid, x, y))
                {
                    continue;
                }

                lit.Add((column, row));
            }
        }

        return lit;
    }

    /// <summary>
    /// Niveau lissé de la musique : monte vite (une frappe se voit tout de
    /// suite), redescend lentement (la trame ne clignote pas).
    /// </summary>
    public static double Smooth(double previous, double peak)
    {
        double target = Math.Clamp(Math.Sqrt(Math.Max(0, peak)), 0, 1);
        return target > previous ? previous + ((target - previous) * 0.6) : previous + ((target - previous) * 0.15);
    }

    private static bool Touches(IReadOnlyList<TrameRect> avoid, double x, double y)
    {
        for (int i = 0; i < avoid.Count; i++)
        {
            if (avoid[i].Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Le point est-il dans la silhouette, avec une demi-cellule de marge ?</summary>
    private static bool Inside(double x, double y, double width, double height, double radius, double shoulder, double cell)
    {
        double margin = cell / 2;
        double left = shoulder + margin, right = width - shoulder - margin, bottom = height - margin;

        if (x < left || x > right || y > bottom)
        {
            return false;
        }

        double r = Math.Max(0, radius - margin);
        double cy = bottom - r;

        if (y <= cy)
        {
            return true;
        }

        double cx = x < left + r ? left + r : x > right - r ? right - r : x;
        return Math.Pow(x - cx, 2) + Math.Pow(y - cy, 2) <= r * r;
    }
}
