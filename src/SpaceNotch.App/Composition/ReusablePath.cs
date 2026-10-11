using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Scenes;
using Windows.Foundation;

namespace SpaceNotch_App.Composition;

/// <summary>
/// Un tracé fermé, créé une fois puis mis à jour sur place : seuls son point de
/// départ et ses points changent, aucun objet XAML n'est créé par image.
///
/// <para>
/// Un <c>PathGeometry</c>, un <c>PathFigure</c> et un <c>PolyLineSegment</c> neufs
/// à chaque image du ressort faisaient demander par XAML un GC de génération 2
/// à chaque transition — raison « provoqué non forcé », pause de 12 ms en
/// moyenne (visite <c>--frames</c> du 2026-10-07, n° 46). Une géométrie ne sert
/// qu'à un seul <c>Path</c> : un <see cref="ReusablePath"/> par usage.
/// </para>
/// </summary>
internal sealed class ReusablePath
{
    private PathGeometry? _geometry;
    private PathFigure? _figure;
    private PointCollection? _points;

    /// <summary>Le tracé, mis à jour ; <c>null</c> pour un contour vide (le précédent reste).</summary>
    public PathGeometry? Set(ShapePoint[] points)
    {
        if (points.Length == 0)
        {
            return null;
        }

        if (_geometry is null || _figure is null || _points is null)
        {
            var segment = new PolyLineSegment();
            _figure = new PathFigure { IsClosed = true, IsFilled = true };
            _figure.Segments.Add(segment);
            _geometry = new PathGeometry();
            _geometry.Figures.Add(_figure);
            _points = segment.Points;
        }

        _figure.StartPoint = new Point(points[0].X, points[0].Y);

        int count = points.Length - 1;

        while (_points.Count > count)
        {
            _points.RemoveAt(_points.Count - 1);
        }

        for (int i = 0; i < count; i++)
        {
            var point = new Point(points[i + 1].X, points[i + 1].Y);

            if (i < _points.Count)
            {
                _points[i] = point;
            }
            else
            {
                _points.Add(point);
            }
        }

        return _geometry;
    }
}
