using System;
using System.Collections.Generic;
using SpaceNotch.Core.Presentation;

namespace SpaceNotch.Core.Scenes;

/// <summary>
/// La goutte qui s'étire : ce qui relie le bord de l'écran à la notch qu'on
/// arrache, puis se rompt.
///
/// <para>
/// Trois formes, toutes noires, tracées l'une sur l'autre : la <b>trace</b>
/// restée accrochée au bord (une notch qui se résorbe), le <b>fil</b> qui la
/// relie à la pastille, et la pastille elle-même. Une surface opaque unie à
/// une autre surface opaque de même couleur ne laisse voir aucune jointure : la
/// superposition donne l'union, sans calcul de métaballes ni effet de flou.
/// </para>
///
/// <para>
/// Le fil est un sablier : ses deux bouts épousent la largeur des formes qu'il
/// relie, sa taille s'amincit avec l'avancement, et il se rompt en
/// <see cref="BreakAt"/>. Les deux moitiés deviennent alors deux pointes qui
/// rentrent chacune dans sa forme. Le raccrochage rejoue la même fonction à
/// l'envers. Voir ADR-019.
/// </para>
/// </summary>
public static class GooBridge
{
    /// <summary>Avancement auquel le fil se rompt.</summary>
    public const double BreakAt = 0.55;

    /// <summary>Durée relative, après la rupture, pendant laquelle les pointes se rétractent.</summary>
    public const double SpikeSpan = 0.30;

    /// <summary>Durée d'un arrachement complet, en secondes.</summary>
    public const double TearSeconds = 0.34;

    /// <summary>Durée d'un raccrochage complet, en secondes : l'aspiration est un peu plus vive.</summary>
    public const double ReattachSeconds = 0.28;

    /// <summary>Largeur des bouts du fil, relative à la plus étroite des deux formes.</summary>
    public const double NeckWidthRatio = 0.5;

    /// <summary>Taille du fil au départ, relative à ses bouts : déjà pincé, jamais un tube.</summary>
    public const double InitialWaist = 0.7;

    private const int Samples = 16;

    /// <summary>Avancement de la résorption de la trace : elle commence avant la rupture.</summary>
    public static double ResidueProgress(double t)
        => SmoothStep(Math.Clamp((t - 0.3) / 0.7, 0, 1));

    /// <summary>
    /// Trace restée au bord à l'avancement <paramref name="t"/> : moins haute,
    /// moins large, jusqu'à disparaître.
    /// </summary>
    public static IslandFootprint Residue(IslandFootprint attached, double t)
    {
        double p = ResidueProgress(t);

        return new IslandFootprint(attached.Width * (1 - (0.5 * p)), attached.Height * (1 - p));
    }

    /// <summary>Taille du fil, en DIPs, pour des bouts de largeur <paramref name="ends"/>.</summary>
    public static double Waist(double ends, double t)
        => ends * InitialWaist * Math.Max(0, 1 - (Math.Clamp(t, 0, 1) / BreakAt));

    /// <summary>
    /// Profondeur à laquelle les bouts du fil plongent dans chaque forme, en
    /// DIPs. Les bouts s'évasent jusqu'à l'horizontale : cachés sous la
    /// surface, ils ne laissent voir qu'un congé qui naît de la forme.
    /// </summary>
    public const double Inset = 6;

    /// <summary>
    /// Contours du fil à l'avancement <paramref name="t"/>, dans le repère de
    /// l'écran : un sablier avant la rupture, deux pointes après, rien une fois
    /// les pointes rentrées.
    ///
    /// <para>
    /// Chaque flanc est un arc d'ellipse : horizontal aux bouts — il épouse la
    /// surface qu'il quitte, comme un congé —, vertical à la taille. C'est ce
    /// qui distingue une goutte d'un tube posé entre deux formes : un profil
    /// qui arrive en biais dessinerait deux « ailes » à la jointure.
    /// </para>
    /// </summary>
    /// <param name="residue">Trace accrochée au bord, en coordonnées d'écran.</param>
    /// <param name="pill">Pastille, en coordonnées d'écran.</param>
    /// <param name="t">Avancement, de 0 (fusion) à 1 (séparation complète).</param>
    public static IReadOnlyList<ShapePoint[]> Neck(ScreenRect residue, ScreenRect pill, double t)
    {
        t = Math.Clamp(t, 0, 1);

        double ends = Math.Min(residue.Width, pill.Width) * NeckWidthRatio;

        if (ends <= 0 || pill.IsEmpty || residue.Height <= 0.5)
        {
            return [];
        }

        // Le fil va du bas de la trace au haut de la pastille, et plonge un peu
        // dans chacune. Si les deux se recouvrent encore, il comble la jointure
        // entre leurs congés au lieu de disparaître.
        double y0 = Math.Min(residue.Bottom, pill.Y) - Inset;
        double y1 = Math.Max(residue.Bottom, pill.Y) + Inset;

        // Une pastille remontée au-dessus de la trace n'est plus reliée par le bas.
        if (pill.CenterY <= residue.Bottom - (residue.Height / 2))
        {
            return [];
        }

        double x0 = residue.CenterX;
        double x1 = pill.CenterX;

        if (t < BreakAt)
        {
            return [Hourglass(x0, y0, x1, y1, ends, Waist(ends, t))];
        }

        double p = Math.Clamp((t - BreakAt) / SpikeSpan, 0, 1);

        if (p >= 1)
        {
            return [];
        }

        // Chaque moitié se rétracte vers sa forme en s'effilant ; la plus
        // lourde — la pastille — garde sa pointe un peu plus longtemps.
        double half = (y1 - y0) / 2;
        double upper = half * Math.Pow(1 - p, 1.6);
        double lower = half * Math.Pow(1 - p, 1.2);

        var spikes = new List<ShapePoint[]>(2);

        if (upper > 0.5)
        {
            spikes.Add(Spike(x0, y0, x0 + ((x1 - x0) * 0.25 * (1 - p)), y0 + upper, ends));
        }

        if (lower > 0.5)
        {
            spikes.Add(Spike(x1, y1, x1 + ((x0 - x1) * 0.25 * (1 - p)), y1 - lower, ends));
        }

        return spikes;
    }

    /// <summary>
    /// Sablier : deux arcs d'ellipse par flanc, horizontaux aux bouts,
    /// verticaux à la taille.
    /// </summary>
    private static ShapePoint[] Hourglass(double x0, double y0, double x1, double y1, double ends, double waist)
    {
        var left = new ShapePoint[Samples + 1];
        var right = new ShapePoint[Samples + 1];
        double flare = Math.Max(0, (ends - waist) / 2);

        for (int i = 0; i <= Samples; i++)
        {
            double s = (double)i / Samples;
            double y = y0 + ((y1 - y0) * s);
            double cx = x0 + ((x1 - x0) * SmoothStep(s));
            double u = Math.Abs((2 * s) - 1);
            double half = (waist / 2) + (flare * Fillet(u));

            left[i] = new ShapePoint(cx - half, y);
            right[i] = new ShapePoint(cx + half, y);
        }

        return Clockwise(left, right);
    }

    /// <summary>
    /// Pointe : la moitié d'un sablier rompu. Évasée à l'horizontale sur sa
    /// forme, arrondie à la pointe, comme une goutte qui retombe.
    /// </summary>
    private static ShapePoint[] Spike(double baseX, double baseY, double tipX, double tipY, double ends)
    {
        var left = new ShapePoint[Samples + 1];
        var right = new ShapePoint[Samples + 1];

        for (int i = 0; i <= Samples; i++)
        {
            double s = (double)i / Samples;
            double y = baseY + ((tipY - baseY) * s);
            double cx = baseX + ((tipX - baseX) * s);
            double half = ends / 2 * Fillet(1 - s);

            left[i] = new ShapePoint(cx - half, y);
            right[i] = new ShapePoint(cx + half, y);
        }

        return Clockwise(left, right);
    }

    /// <summary>
    /// Profil de congé : 0 à la taille (<paramref name="u"/> = 0, tangente
    /// verticale), 1 au bout (<paramref name="u"/> = 1, tangente horizontale).
    /// C'est un quart d'ellipse, <c>1 − √(1 − u²)</c>.
    /// </summary>
    public static double Fillet(double u)
    {
        u = Math.Clamp(u, 0, 1);
        return 1 - Math.Sqrt(1 - (u * u));
    }

    /// <summary>
    /// Contour fermé dans le sens horaire à l'écran, quel que soit le sens des
    /// flancs. Le sens n'est pas un détail : la trace et la pastille sont
    /// horaires, et un fil tracé dans l'autre sens creuserait un trou là où il
    /// les recouvre, au lieu de s'y fondre.
    /// </summary>
    private static ShapePoint[] Clockwise(ShapePoint[] left, ShapePoint[] right)
    {
        var outline = new ShapePoint[left.Length + right.Length];

        for (int i = 0; i < left.Length; i++)
        {
            outline[i] = left[i];
            outline[left.Length + i] = right[right.Length - 1 - i];
        }

        if (SignedArea(outline) < 0)
        {
            Array.Reverse(outline);
        }

        return outline;
    }

    /// <summary>Aire signée, positive pour un contour horaire dans un repère dont l'axe y descend.</summary>
    public static double SignedArea(IReadOnlyList<ShapePoint> outline)
    {
        ArgumentNullException.ThrowIfNull(outline);

        double area = 0;

        for (int i = 0; i < outline.Count; i++)
        {
            ShapePoint a = outline[i];
            ShapePoint b = outline[(i + 1) % outline.Count];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        return area / 2;
    }

    private static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - (2 * x));
    }
}
