using System;

namespace NotchFlow.Core.Scenes;

/// <summary>
/// Un point du contour, en DIPs, origine au coin supérieur gauche de l'Island.
/// </summary>
public readonly record struct ShapePoint(double X, double Y);

/// <summary>
/// Construction de la silhouette de l'Island.
///
/// <para>
/// <b>Une seule famille de courbes.</b> Le congé du bas n'est pas un arc de
/// cercle : c'est une superellipse <c>|x/r|^n + |y/r|^n = 1</c> dont l'exposant
/// <c>n = 2K</c> est le seul paramètre. <c>K = 1</c> redonne exactement le cercle,
/// <c>K = 2</c> donne le squircle des icônes d'iOS — celui que Figma expose comme
/// « corner smoothing » et que CSS nomme <c>corner-shape: squircle</c>.
/// </para>
///
/// <para>
/// <b>Pourquoi ce n'est pas un détail à 12 DIP.</b> Au milieu du congé, l'écart
/// entre un arc et un squircle est inférieur au pixel. Il ne l'est pas à la
/// jonction avec l'arête : un arc de cercle y produit une discontinuité de
/// courbure — le petit « accroc » qu'on perçoit sans savoir le nommer — que la
/// superellipse supprime. Le paramètre est aussi ce qui permet à une maquette et
/// au code de partager un nombre au lieu de diverger.
/// </para>
///
/// <para>
/// Les coins supérieurs restent droits : c'est la règle de géométrie de Windows,
/// où deux arêtes droites qui se rencontrent ne sont pas arrondies, et c'est
/// aussi ce qui fait tenir l'Island au bord de l'écran.
/// </para>
///
/// <para>
/// <b>Ce que cette classe ne fait pas encore.</b> Un exposant négatif décrirait un
/// congé <em>concave</em> — les épaules qui raccorderaient un jour l'Island à une
/// bande de bord d'écran. Ce n'est pas une variation d'exposant : un congé concave
/// s'ajoute à l'extérieur de la silhouette, il ne se substitue pas à un coin
/// convexe. Tant qu'aucune bande de ce type n'existe, la prétendre ici serait une
/// branche morte qu'aucun test ne couvrirait.
/// </para>
/// </summary>
public static class IslandShape
{
    /// <summary>Arc de cercle : <c>K = 1</c>.</summary>
    public const double Circular = 1.0;

    /// <summary>Squircle d'iOS : <c>K = 2</c>.</summary>
    public const double Squircle = 2.0;

    /// <summary>
    /// Nombre de segments par congé. Dix suffisent très largement : à un rayon de
    /// 24 DIP, l'écart maximal entre la corde et la courbe reste sous le
    /// vingtième de pixel.
    /// </summary>
    private const int SegmentsPerCorner = 10;

    /// <summary>Points produits par <see cref="Silhouette"/> pour un contour complet.</summary>
    public static int PointCount => 3 + (2 * (SegmentsPerCorner + 1));

    /// <summary>
    /// Contour de l'Island, dans le sens des aiguilles d'une montre, à partir du
    /// coin supérieur gauche.
    /// </summary>
    /// <param name="width">Largeur en DIPs.</param>
    /// <param name="height">Hauteur en DIPs.</param>
    /// <param name="radius">Rayon des congés du bas, en DIPs.</param>
    /// <param name="smoothing">
    /// Exposant de la superellipse. <see cref="Circular"/> pour un arc,
    /// <see cref="Squircle"/> pour le squircle.
    /// </param>
    /// <param name="band">
    /// Hauteur maximale, en DIPs. Zéro pour le contour complet.
    ///
    /// Une valeur positive tronque le contour : le reflet spéculaire est ainsi
    /// découpé sans second calculateur de forme et sans intersection de
    /// géométries. La silhouette étant convexe, borner les ordonnées de ses
    /// sommets à la hauteur voulue donne exactement l'intersection du contour et
    /// d'un demi-plan — aucun point à recalculer.
    /// </param>
    public static ShapePoint[] Silhouette(
        double width,
        double height,
        double radius,
        double smoothing = Squircle,
        double band = 0)
    {
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        double w = width;
        double h = height;
        double r = Math.Clamp(radius, 0, Math.Min(w, h) / 2);
        double k = smoothing < Circular ? Circular : smoothing;

        if (r <= 0)
        {
            return Limit(
                [
                    new ShapePoint(0, 0),
                    new ShapePoint(w, 0),
                    new ShapePoint(w, h),
                    new ShapePoint(0, h)
                ],
                band);
        }

        var points = new ShapePoint[PointCount];
        int index = 0;

        // Bord supérieur, d'un coin droit à l'autre : aucune courbure.
        points[index++] = new ShapePoint(0, 0);
        points[index++] = new ShapePoint(w, 0);

        // Épaule droite jusqu'au début du congé.
        points[index++] = new ShapePoint(w, h - r);

        // Congé inférieur droit : de l'horizontale à la verticale.
        for (int i = 0; i <= SegmentsPerCorner; i++)
        {
            double t = Math.PI / 2 * i / SegmentsPerCorner;
            points[index++] = new ShapePoint(
                w - r + (r * Component(Math.Cos(t), k)),
                h - r + (r * Component(Math.Sin(t), k)));
        }

        // Congé inférieur gauche : de la verticale à l'horizontale, donc dans
        // l'ordre inverse pour rester horaire. Le dernier point referme sur
        // l'épaule gauche, qui n'a pas besoin d'être ajoutée deux fois.
        for (int i = SegmentsPerCorner; i >= 0; i--)
        {
            double t = Math.PI / 2 * i / SegmentsPerCorner;
            points[index++] = new ShapePoint(
                r - (r * Component(Math.Cos(t), k)),
                h - r + (r * Component(Math.Sin(t), k)));
        }

        return Limit(points, band);
    }

    /// <summary>
    /// Composante d'une superellipse : <c>c^(1/K)</c>.
    ///
    /// L'élévation à la puissance <c>1/K</c> est ce qui distingue les familles de
    /// congés : <c>K = 1</c> laisse le cosinus intact et décrit un cercle,
    /// <c>K = 2</c> en prend la racine carrée et épaissit le coin jusqu'au
    /// squircle, une valeur plus grande aplatit les flancs et rapproche le coin
    /// d'un angle droit.
    /// </summary>
    private static double Component(double value, double k)
        => Math.Pow(Math.Clamp(value, 0, 1), 1 / k);

    /// <summary>Réduit le contour à la hauteur demandée. Zéro laisse le contour intact.</summary>
    private static ShapePoint[] Limit(ShapePoint[] points, double band)
    {
        if (band <= 0)
        {
            return points;
        }

        for (int i = 0; i < points.Length; i++)
        {
            if (points[i].Y > band)
            {
                points[i] = points[i] with { Y = band };
            }
        }

        return points;
    }
}
