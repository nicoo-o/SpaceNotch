using System.Collections.Generic;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Scenes;
using Windows.Foundation;

namespace SpaceNotch_App.Composition;

/// <summary>
/// Convertit le contour calculé par le cœur en géométrie de tracé XAML.
///
/// <para>
/// La séparation est volontaire : le calcul du contour ne connaît ni XAML ni
/// Windows, donc il se teste sans interface — c'est ce qui permet de vérifier
/// qu'une superellipse passe bien par ses extrémités et qu'un rayon nul dégénère
/// en rectangle. Cette classe-ci ne fait que traduire, et c'est la seule chose
/// qu'elle fait.
/// </para>
///
/// <para>
/// <b>Pourquoi un seuil et pas un test d'égalité.</b> Le ressort produit une
/// centaine d'images par transition, dont beaucoup à moins d'un dixième de DIP
/// l'une de l'autre. Reconstruire un tracé pour ces écarts coûterait une
/// allocation et une tessellation par image sans rien changer à l'écran. Le seuil
/// est en dessous de ce que l'œil distingue, donc la mémoire ne se voit pas.
/// </para>
/// </summary>
internal sealed class IslandGeometryFactory
{
    /// <summary>Écart minimal, en DIPs, justifiant de reconstruire un tracé.</summary>
    private const double RebuildThreshold = 0.05;

    private double _width = double.NaN;
    private double _height = double.NaN;
    private double _radius = double.NaN;
    private double _smoothing = double.NaN;
    private double _shoulder = double.NaN;
    private bool _floating;

    /// <summary>Nombre de tracés effectivement reconstruits. Sert de preuve au repos.</summary>
    public long Rebuilds { get; private set; }

    /// <summary>
    /// Contour complet de l'Island. Renvoie <c>null</c> si celui-ci est inchangé
    /// depuis le dernier appel, auquel cas l'appelant conserve sa géométrie.
    /// </summary>
    /// <param name="footprint">Encombrement, épaules comprises.</param>
    /// <param name="radius">Rayon des congés du bas, déjà résolu pour cette hauteur.</param>
    /// <param name="smoothing">Exposant de la superellipse.</param>
    /// <param name="band">Hauteur de la bande de reflet, ou zéro pour le contour complet.</param>
    /// <param name="shoulder">Rayon des épaules concaves au bord de l'écran.</param>
    public Geometry? Build(
        IslandFootprint footprint,
        double radius,
        double smoothing,
        double band = 0,
        double shoulder = 0,
        bool floating = false)
    {
        if (floating)
        {
            if (_floating && Unchanged(footprint, radius, smoothing, 0))
            {
                return null;
            }

            _floating = true;
            _width = footprint.Width;
            _height = footprint.Height;
            _radius = radius;
            _smoothing = smoothing;
            _shoulder = 0;

            Rebuilds++;

            return FromPoints(IslandShape.Floating(footprint.Width, footprint.Height, radius, smoothing));
        }

        if (band > 0)
        {
            // Un tracé borné — le reflet — n'est pas mémorisé : il dépend de la
            // même géométrie et se recalcule à chaque fois que la forme change,
            // ce qui est déjà exceptionnel.
            return BuildCore(footprint, radius, smoothing, band, shoulder);
        }

        if (!_floating && Unchanged(footprint, radius, smoothing, shoulder))
        {
            return null;
        }

        _floating = false;
        _width = footprint.Width;
        _height = footprint.Height;
        _radius = radius;
        _smoothing = smoothing;
        _shoulder = shoulder;

        Rebuilds++;

        return BuildCore(footprint, radius, smoothing, band, shoulder);
    }

    /// <summary>Force la reconstruction au prochain appel.</summary>
    public void Forget()
    {
        _floating = false;
        _width = double.NaN;
        _height = double.NaN;
        _radius = double.NaN;
        _smoothing = double.NaN;
        _shoulder = double.NaN;
    }

    private bool Unchanged(IslandFootprint footprint, double radius, double smoothing, double shoulder)
        => Math.Abs(footprint.Width - _width) < RebuildThreshold
            && Math.Abs(footprint.Height - _height) < RebuildThreshold
            && Math.Abs(radius - _radius) < RebuildThreshold
            && Math.Abs(smoothing - _smoothing) < RebuildThreshold
            && Math.Abs(shoulder - _shoulder) < RebuildThreshold;

    private static PathGeometry? BuildCore(
        IslandFootprint footprint,
        double radius,
        double smoothing,
        double band,
        double shoulder)
    {
        return FromPoints(IslandShape.Silhouette(
            footprint.Width, footprint.Height, radius, smoothing, band, shoulder));
    }

    /// <summary>
    /// Réunit plusieurs contours dans un même tracé, décalés d'une origine —
    /// la trace, le fil et ses pointes de la goutte. Des contours opaques qui se
    /// recouvrent se lisent comme une seule matière.
    /// </summary>
    public static Geometry? FromPolygons(IEnumerable<ShapePoint[]> polygons, double originX, double originY)
    {
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };

        foreach (ShapePoint[] points in polygons)
        {
            if (points.Length < 3)
            {
                continue;
            }

            var figure = new PathFigure
            {
                StartPoint = new Point(points[0].X - originX, points[0].Y - originY),
                IsClosed = true,
                IsFilled = true
            };

            var segment = new PolyLineSegment();

            for (int i = 1; i < points.Length; i++)
            {
                segment.Points.Add(new Point(points[i].X - originX, points[i].Y - originY));
            }

            figure.Segments.Add(segment);
            geometry.Figures.Add(figure);
        }

        return geometry.Figures.Count == 0 ? null : geometry;
    }

    private static PathGeometry? FromPoints(ShapePoint[] points)
    {
        // Une forme vide — encombrement nul pendant la construction — ne produit
        // aucun tracé : mieux vaut garder le précédent qu'indexer un tableau vide.
        if (points.Length == 0)
        {
            return null;
        }

        var figure = new PathFigure
        {
            StartPoint = new Point(points[0].X, points[0].Y),
            IsClosed = true,
            IsFilled = true
        };

        var segment = new PolyLineSegment();

        for (int i = 1; i < points.Length; i++)
        {
            segment.Points.Add(new Point(points[i].X, points[i].Y));
        }

        figure.Segments.Add(segment);

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return geometry;
    }
}
