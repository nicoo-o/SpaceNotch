using System;
using System.Collections.Generic;
using SpaceNotch.Core.Scenes;

namespace SpaceNotch.Core.Presentation;

/// <summary>Bord de l'écran auquel la notch est accrochée.</summary>
public enum NotchEdge
{
    /// <summary>Le haut : la notch de référence (ADR-017).</summary>
    Top = 0,

    /// <summary>Le côté gauche : une languette verticale (ADR-020).</summary>
    Left = 1,

    /// <summary>Le côté droit : une languette verticale (ADR-020).</summary>
    Right = 2
}

/// <summary>Taille d'un élément secondaire — la bulle, la languette.</summary>
public enum ElementSize
{
    Small = 0,
    Normal = 1,
    Large = 2
}

/// <summary>
/// Repère d'un bord : toute la géométrie accrochée est calculée comme si le
/// bord était en haut — une coordonnée <c>u</c> le long du bord, une
/// profondeur <c>v</c> vers l'intérieur de l'écran — puis tournée vers le bord
/// réel. La notch du haut, la languette de gauche et celle de droite sont donc
/// la même silhouette, avec les mêmes épaules et les mêmes congés.
/// </summary>
public static class EdgeFrame
{
    /// <summary>Vrai pour les bords latéraux.</summary>
    public static bool IsSide(NotchEdge edge) => edge is NotchEdge.Left or NotchEdge.Right;

    /// <summary>
    /// Point du repère du bord vers le repère de l'écran.
    /// </summary>
    /// <param name="edge">Bord.</param>
    /// <param name="u">Coordonnée le long du bord.</param>
    /// <param name="v">Profondeur depuis le bord.</param>
    /// <param name="screen">Écran, dans le repère de l'écran.</param>
    public static ShapePoint ToScreen(NotchEdge edge, double u, double v, ScreenRect screen) => edge switch
    {
        NotchEdge.Left => new ShapePoint(screen.X + v, screen.Y + u),
        NotchEdge.Right => new ShapePoint(screen.Right - v, screen.Y + u),
        _ => new ShapePoint(screen.X + u, screen.Y + v)
    };

    /// <summary>Rectangle de l'écran vers le repère du bord.</summary>
    public static ScreenRect ToLocal(NotchEdge edge, ScreenRect rect, ScreenRect screen) => edge switch
    {
        NotchEdge.Left => new ScreenRect(rect.Y - screen.Y, rect.X - screen.X, rect.Height, rect.Width),
        NotchEdge.Right => new ScreenRect(rect.Y - screen.Y, screen.Right - rect.Right, rect.Height, rect.Width),
        _ => new ScreenRect(rect.X - screen.X, rect.Y - screen.Y, rect.Width, rect.Height)
    };

    /// <summary>Rectangle du repère du bord vers l'écran.</summary>
    public static ScreenRect ToScreenRect(NotchEdge edge, ScreenRect local, ScreenRect screen) => edge switch
    {
        NotchEdge.Left => new ScreenRect(screen.X + local.Y, screen.Y + local.X, local.Height, local.Width),
        NotchEdge.Right => new ScreenRect(screen.Right - local.Y - local.Height, screen.Y + local.X, local.Height, local.Width),
        _ => new ScreenRect(screen.X + local.X, screen.Y + local.Y, local.Width, local.Height)
    };

    /// <summary>
    /// Contour du repère du bord vers l'écran, remis dans le sens horaire : une
    /// réflexion — le passage au bord gauche — inverse le sens, et toutes les
    /// pièces d'une même matière doivent tourner dans le même sens pour que
    /// leur superposition soit leur union.
    /// </summary>
    public static ShapePoint[] ToScreen(NotchEdge edge, IReadOnlyList<ShapePoint> local, double originU, ScreenRect screen)
    {
        ArgumentNullException.ThrowIfNull(local);

        var points = new ShapePoint[local.Count];

        for (int i = 0; i < local.Count; i++)
        {
            points[i] = ToScreen(edge, local[i].X + originU, local[i].Y, screen);
        }

        if (GooBridge.SignedArea(points) < 0)
        {
            Array.Reverse(points);
        }

        return points;
    }

    /// <summary>
    /// Encombrement dessiné contre un bord, pour un encombrement exprimé comme
    /// en haut. Sur un côté, les épaules passent de la largeur à la hauteur :
    /// le contenu garde la place que sa scène a demandée.
    /// </summary>
    public static IslandFootprint Oriented(IslandFootprint footprint, NotchEdge edge, double shoulder)
    {
        if (!IsSide(edge) || !footprint.IsValid)
        {
            return footprint;
        }

        double s = IslandShape.EffectiveShoulder(footprint.Width, footprint.Height, shoulder);

        return new IslandFootprint(Math.Max(1, footprint.Width - (2 * s)), footprint.Height + (2 * s));
    }

    /// <summary>
    /// Silhouette d'une forme accrochée à un bord, dans le repère de la
    /// fenêtre qui l'entoure exactement (origine en haut à gauche).
    /// </summary>
    /// <param name="geometry">Géométrie de la notch.</param>
    /// <param name="drawn">Encombrement dessiné, orienté comme à l'écran.</param>
    /// <param name="edge">Bord.</param>
    /// <param name="shoulder">Épaule à utiliser pour ce bord.</param>
    public static ShapePoint[] Silhouette(NotchGeometry geometry, IslandFootprint drawn, NotchEdge edge, double shoulder)
    {
        if (!IsSide(edge))
        {
            return (geometry with { Shoulder = shoulder }).Silhouette(drawn);
        }

        // Le long du bord : la hauteur ; vers l'intérieur : la largeur.
        var local = new IslandFootprint(drawn.Height, drawn.Width);
        ShapePoint[] points = (geometry with { Shoulder = shoulder }).Silhouette(local);

        return ToScreen(edge, points, 0, new ScreenRect(0, 0, drawn.Width, drawn.Height));
    }
}

/// <summary>
/// La languette : la notch accrochée à un côté de l'écran. Au repos, elle ne
/// montre que l'icône ou la grille de son activité ; elle s'ouvre vers le
/// centre de l'écran, avec le contenu à l'horizontale. Voir ADR-020.
/// </summary>
public static class SideTab
{
    /// <summary>Épaule par défaut de la languette.</summary>
    public const double DefaultShoulder = 10;

    /// <summary>Profondeur au repos, depuis le bord, pour chaque taille.</summary>
    public static double Depth(ElementSize size) => size switch
    {
        ElementSize.Small => 24,
        ElementSize.Large => 34,
        _ => 28
    };

    /// <summary>Longueur au repos le long du bord, épaules non comprises.</summary>
    public static double Length(ElementSize size) => size switch
    {
        ElementSize.Small => 60,
        ElementSize.Large => 96,
        _ => 76
    };

    /// <summary>
    /// Languette au repos, exprimée comme un encombrement « du haut » : une
    /// fois orientée (<see cref="EdgeFrame.Oriented"/>), elle mesure sa
    /// profondeur en largeur et sa longueur plus deux épaules en hauteur.
    /// </summary>
    public static IslandFootprint Rest(ElementSize size, double shoulder)
    {
        double s = Math.Max(0, shoulder);
        return new IslandFootprint(Depth(size) + (2 * s), Length(size));
    }

    /// <summary>Aperçu au survol : elle s'avance un peu vers l'intérieur.</summary>
    public static IslandFootprint Preview(IslandFootprint rest)
        => new(rest.Width * 1.25, rest.Height * 1.08);

    /// <summary>
    /// Rectangle d'une forme accrochée à un côté, centré sur la position de la
    /// languette le long du bord et gardé dans l'écran.
    /// </summary>
    /// <param name="edge">Bord latéral.</param>
    /// <param name="drawn">Encombrement orienté.</param>
    /// <param name="offset">Position le long du bord, de 0 (haut) à 1 (bas).</param>
    /// <param name="screen">Écran.</param>
    /// <param name="work">Zone de travail, pour garder la forme hors de la barre des tâches.</param>
    public static ScreenRect Place(NotchEdge edge, IslandFootprint drawn, double offset, ScreenRect screen, ScreenRect work)
    {
        double center = work.Y + (Math.Clamp(offset, 0, 1) * work.Height);
        double y = Math.Clamp(center - (drawn.Height / 2), work.Y, Math.Max(work.Y, work.Bottom - drawn.Height));
        double x = edge == NotchEdge.Right ? screen.Right - drawn.Width : screen.X;

        return new ScreenRect(x, y, drawn.Width, drawn.Height);
    }

    /// <summary>Position le long du bord, de 0 à 1, d'un centre vertical donné.</summary>
    public static double OffsetOf(double centerY, ScreenRect work)
        => work.Height <= 0 ? 0.5 : Math.Clamp((centerY - work.Y) / work.Height, 0, 1);
}
