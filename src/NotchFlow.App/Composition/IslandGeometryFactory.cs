using Microsoft.UI.Xaml.Media;
using NotchFlow.Core.Scenes;
using Windows.Foundation;

namespace NotchFlow_App.Composition;

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
        double shoulder = 0)
    {
        if (band > 0)
        {
            // Un tracé borné — le reflet — n'est pas mémorisé : il dépend de la
            // même géométrie et se recalcule à chaque fois que la forme change,
            // ce qui est déjà exceptionnel.
            return BuildCore(footprint, radius, smoothing, band, shoulder);
        }

        if (Unchanged(footprint, radius, smoothing, shoulder))
        {
            return null;
        }

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
        ShapePoint[] points = IslandShape.Silhouette(
            footprint.Width, footprint.Height, radius, smoothing, band, shoulder);

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
