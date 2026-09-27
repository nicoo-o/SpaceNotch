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
    /// <summary>Retrait de l'anneau par rapport au bord de la notch, en DIPs.</summary>
    public const double RingInset = 5;

    /// <summary>
    /// L'anneau sur lequel le temps se lit : le corps de la notch — épaules
    /// exclues — rentré de <paramref name="inset"/>, avec des congés du bas
    /// rentrés d'autant. Il reste entièrement à l'intérieur : rien ne se dessine
    /// autour de la notch. Même sens que la silhouette ; vide si la notch est
    /// trop petite pour le porter.
    /// </summary>
    public static ShapePoint[] Ring(double width, double height, double radius, double smoothing, double shoulder, double inset = RingInset)
    {
        double s = IslandShape.EffectiveShoulder(width, height, shoulder);
        double r = IslandShape.EffectiveRadius(width, height, radius, shoulder);
        double w = width - (2 * s) - (2 * inset);
        double h = height - (2 * inset);

        if (w <= 2 * inset || h <= 2 * inset)
        {
            return [];
        }

        ShapePoint[] ring = IslandShape.Silhouette(w, h, Math.Max(0, r - inset), smoothing);

        for (int i = 0; i < ring.Length; i++)
        {
            ring[i] = new ShapePoint(ring[i].X + s + inset, ring[i].Y + inset);
        }

        // Le fil part exactement du milieu du bord bas : ce point est ajouté
        // sur le segment droit qui le traverse, s'il n'y est pas déjà.
        double center = width / 2;
        double bottom = height - inset;

        for (int i = 0; i < ring.Length; i++)
        {
            ShapePoint a = ring[i], b = ring[(i + 1) % ring.Length];

            if (Math.Abs(a.Y - bottom) < 0.01 && Math.Abs(b.Y - bottom) < 0.01
                && Math.Min(a.X, b.X) < center - 0.01 && Math.Max(a.X, b.X) > center + 0.01)
            {
                var list = new List<ShapePoint>(ring);
                list.Insert(i + 1, new ShapePoint(center, bottom));
                return [.. list];
            }
        }

        return ring;
    }

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

/// <summary>Test d'appartenance d'un point à la silhouette (polygone fermé).</summary>
public static class ShapeHit
{
    /// <summary>Le point est-il dans le polygone ? Règle pair-impair.</summary>
    public static bool Contains(IReadOnlyList<ShapePoint> outline, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(outline);
        bool inside = false;

        for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
        {
            ShapePoint a = outline[i], b = outline[j];

            if ((a.Y > y) != (b.Y > y) && x < ((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
