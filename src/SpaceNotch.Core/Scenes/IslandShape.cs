using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Scenes;

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
/// <b>Les épaules.</b> Un exposant négatif ne décrit pas un congé concave : un
/// congé concave s'ajoute <em>à l'extérieur</em> de la silhouette. C'est
/// exactement ce que sont les épaules — deux quarts de courbe tangents au bord
/// de l'écran d'un côté, au flanc de la notch de l'autre. Ce sont elles qui font
/// lire la forme comme une découpe descendue du bord, et non comme une capsule
/// posée sous lui : sans épaules, le coin supérieur est un angle droit collé à
/// l'écran ; avec elles, le bord de l'écran « coule » dans la notch. Voir
/// ADR-017.
/// </para>
///
/// <para>
/// <b>Toujours attachée.</b> Aucun point du contour n'a une ordonnée négative, et
/// le bord supérieur occupe toujours toute la largeur à l'ordonnée zéro. Il
/// n'existe pas de paramètre qui décolle la forme du bord : c'est un invariant de
/// la géométrie, vérifié par les tests, pas un réglage.
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

    /// <summary>Points produits par <see cref="Silhouette"/> pour un contour complet, sans épaules.</summary>
    public static int PointCount => 3 + (2 * (SegmentsPerCorner + 1));

    /// <summary>Points produits par <see cref="Silhouette"/> pour un contour complet avec épaules.</summary>
    public static int PointCountWithShoulders => 5 + (4 * SegmentsPerCorner);

    /// <summary>
    /// Rayon de congé effectivement tracé pour une forme donnée.
    ///
    /// La borne n'est pas celle d'un rectangle arrondi — la moitié du plus petit
    /// côté — parce que la notch n'a qu'un bord libre : ses congés peuvent
    /// occuper toute la hauteur sous les épaules. C'est ce qui permet à une
    /// forme compacte de 36 DIP de porter un congé de 26 et de garder la
    /// silhouette organique voulue, là où un rectangle arrondi l'aurait
    /// plafonné à 18.
    /// </summary>
    public static double EffectiveRadius(double width, double height, double radius, double shoulder = 0)
    {
        double s = EffectiveShoulder(width, height, shoulder);
        double limit = Math.Min((width - (2 * s)) / 2, height - s);

        return Math.Clamp(radius, 0, Math.Max(0, limit));
    }

    /// <summary>
    /// Épaule effectivement tracée. Elle ne peut pas dépasser le quart de la
    /// largeur, ni le tiers de la hauteur : au-delà, la notch deviendrait un
    /// entonnoir et le contenu n'aurait plus de place.
    /// </summary>
    public static double EffectiveShoulder(double width, double height, double shoulder)
    {
        if (shoulder <= 0 || width <= 0 || height <= 0)
        {
            return 0;
        }

        return Math.Min(shoulder, Math.Min(width / 4, height / 3));
    }

    /// <summary>
    /// Contour de la notch, dans le sens des aiguilles d'une montre, à partir du
    /// coin supérieur gauche — qui est toujours sur le bord de l'écran.
    /// </summary>
    /// <param name="width">Largeur en DIPs, épaules comprises.</param>
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
    /// géométries. Chaque flanc étant monotone en ordonnée, borner les ordonnées
    /// des sommets à la hauteur voulue donne exactement l'intersection du contour
    /// et d'un demi-plan — aucun point à recalculer.
    /// </param>
    /// <param name="shoulder">
    /// Rayon des épaules concaves qui raccordent la notch au bord de l'écran, en
    /// DIPs. Zéro pour un raccord à angle droit.
    /// </param>
    public static ShapePoint[] Silhouette(
        double width,
        double height,
        double radius,
        double smoothing = Squircle,
        double band = 0,
        double shoulder = 0)
    {
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        double w = width;
        double h = height;
        double s = EffectiveShoulder(w, h, shoulder);
        double r = EffectiveRadius(w, h, radius, s);
        double k = smoothing < Circular ? Circular : smoothing;

        if (r <= 0 && s <= 0)
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

        var points = new List<ShapePoint>(s > 0 ? PointCountWithShoulders : PointCount);

        // Bord supérieur, d'un bout à l'autre : c'est le bord de l'écran, et la
        // notch l'occupe sur toute sa largeur.
        points.Add(new ShapePoint(0, 0));
        points.Add(new ShapePoint(w, 0));

        if (s > 0)
        {
            // Épaule droite : tangente au bord de l'écran en (w, 0), tangente au
            // flanc en (w − s, s). Le centre de courbure est à l'extérieur de la
            // forme, en (w, s) : c'est ce qui la rend concave.
            for (int i = 1; i <= SegmentsPerCorner; i++)
            {
                double t = Math.PI / 2 * i / SegmentsPerCorner;
                points.Add(new ShapePoint(
                    w - (s * Component(Math.Sin(t), k)),
                    s - (s * Component(Math.Cos(t), k))));
            }
        }

        // Flanc droit jusqu'au début du congé.
        double right = w - s;
        double left = s;

        if (r > 0)
        {
            points.Add(new ShapePoint(right, h - r));

            // Congé inférieur droit : de la verticale à l'horizontale.
            for (int i = 0; i <= SegmentsPerCorner; i++)
            {
                double t = Math.PI / 2 * i / SegmentsPerCorner;
                points.Add(new ShapePoint(
                    right - r + (r * Component(Math.Cos(t), k)),
                    h - r + (r * Component(Math.Sin(t), k))));
            }

            // Congé inférieur gauche, dans l'ordre inverse pour rester horaire.
            for (int i = SegmentsPerCorner; i >= 0; i--)
            {
                double t = Math.PI / 2 * i / SegmentsPerCorner;
                points.Add(new ShapePoint(
                    left + r - (r * Component(Math.Cos(t), k)),
                    h - r + (r * Component(Math.Sin(t), k))));
            }
        }
        else
        {
            points.Add(new ShapePoint(right, h));
            points.Add(new ShapePoint(left, h));
        }

        if (s > 0)
        {
            // Remontée du flanc gauche, puis épaule gauche, symétrique de la
            // droite, jusqu'au point de départ exclu.
            points.Add(new ShapePoint(left, s));

            for (int i = SegmentsPerCorner - 1; i >= 1; i--)
            {
                double t = Math.PI / 2 * i / SegmentsPerCorner;
                points.Add(new ShapePoint(
                    s * Component(Math.Sin(t), k),
                    s - (s * Component(Math.Cos(t), k))));
            }
        }

        return Limit([.. points], band);
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
