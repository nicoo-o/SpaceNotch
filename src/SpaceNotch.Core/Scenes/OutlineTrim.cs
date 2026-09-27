using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Scenes;

/// <summary>
/// Pomodoro autour de la silhouette (F11) : le temps restant est une portion
/// du contour réel de la notch, qui se vide à mesure que la session avance.
/// Le tracé part du milieu du bord bas et fait le tour dans le sens horaire.
/// </summary>
public static class OutlineTrim
{
    /// <summary>Longueur totale d'une polyligne fermée.</summary>
    public static double Length(IReadOnlyList<ShapePoint> outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        double length = 0;

        for (int i = 0; i < outline.Count; i++)
        {
            ShapePoint a = outline[i], b = outline[(i + 1) % outline.Count];
            length += Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
        }

        return length;
    }

    /// <summary>
    /// Portion du contour représentant <paramref name="remaining"/> (0..1) du
    /// temps : de l'origine, sur <c>remaining × longueur</c>.
    /// </summary>
    public static IReadOnlyList<ShapePoint> Trim(IReadOnlyList<ShapePoint> outline, double remaining)
    {
        ArgumentNullException.ThrowIfNull(outline);
        var result = new List<ShapePoint>();

        if (outline.Count < 2 || remaining <= 0)
        {
            return result;
        }

        int start = BottomCenterIndex(outline);
        double budget = Length(outline) * Math.Clamp(remaining, 0, 1);
        result.Add(outline[start]);

        for (int k = 0; k < outline.Count && budget > 0; k++)
        {
            ShapePoint a = outline[(start + k) % outline.Count];
            ShapePoint b = outline[(start + k + 1) % outline.Count];
            double segment = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

            if (segment >= budget)
            {
                double f = segment <= 0 ? 0 : budget / segment;
                result.Add(new ShapePoint(a.X + ((b.X - a.X) * f), a.Y + ((b.Y - a.Y) * f)));
                break;
            }

            result.Add(b);
            budget -= segment;
        }

        return result;
    }

    /// <summary>Le point du contour le plus bas et le plus central : l'origine du tracé.</summary>
    private static int BottomCenterIndex(IReadOnlyList<ShapePoint> outline)
    {
        double minX = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        foreach (ShapePoint p in outline)
        {
            minX = Math.Min(minX, p.X);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        double centerX = (minX + maxX) / 2;
        int best = 0;
        double bestScore = double.MaxValue;

        for (int i = 0; i < outline.Count; i++)
        {
            double score = Math.Abs(outline[i].X - centerX) + (Math.Abs(outline[i].Y - maxY) * 4);

            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }
}
