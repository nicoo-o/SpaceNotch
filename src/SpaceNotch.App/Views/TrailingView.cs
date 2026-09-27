using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Activities;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace SpaceNotch_App.Views;

/// <summary>
/// L'élément vivant à droite de la forme compacte (<see cref="CompactTrailing"/>) :
/// un anneau de progression, un arc de batterie ou des barres d'égaliseur.
///
/// <para>
/// Le fil de niveau du volume reste dans la pastille : il a sa propre largeur
/// et son propre défilement. Cette vue ne dessine que les formes carrées.
/// </para>
/// </summary>
public sealed partial class TrailingView : Grid
{
    private const double DefaultSide = 16;
    private const double Stroke = 2;

    /// <summary>Arc de batterie : ouvert en bas, sur trois quarts de tour.</summary>
    private const double BatterySweep = 270;

    private readonly Path _track = new() { StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Path _fill = new() { StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly StackPanel _bars = new() { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Rectangle[] _barCells = new Rectangle[3];
    private CompactTrailing _shown = CompactTrailing.None;
    private Brush? _tint;
    private bool _dancing;
    private double _side = DefaultSide;

    public TrailingView()
    {
        Width = _side;
        Height = _side;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;

        _track.Stroke = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x29, 0xFF, 0xFF, 0xFF));
        Children.Add(_track);
        Children.Add(_fill);

        for (int i = 0; i < _barCells.Length; i++)
        {
            _barCells[i] = new Rectangle { Width = 2, Height = 12, RadiusX = 1, RadiusY = 1, VerticalAlignment = VerticalAlignment.Center };
            _bars.Children.Add(_barCells[i]);
        }

        Children.Add(_bars);
        Unloaded += (_, _) => StopDancing();
    }

    /// <summary>Teinte de l'activité : la partie pleine de l'anneau, les barres.</summary>
    public Brush? Tint
    {
        get => _tint;
        set
        {
            _tint = value;
            _fill.Stroke = value;

            foreach (Rectangle bar in _barCells)
            {
                bar.Fill = value;
            }
        }
    }

    /// <summary>
    /// Diamètre de l'anneau, en DIPs : 16 dans une pastille ; plus grand dans
    /// la bulle, où l'anneau entoure l'icône au lieu de se poser à côté.
    /// </summary>
    public double Diameter
    {
        get => _side;
        set
        {
            if (Math.Abs(value - _side) < 0.1)
            {
                return;
            }

            _side = value;
            Width = value;
            Height = value;

            // La piste est à redessiner à la nouvelle taille.
            CompactTrailing shown = _shown;
            _shown = CompactTrailing.None;
            Show(shown);
        }
    }

    /// <summary>Montre un élément vivant, ou se retire s'il n'y en a pas.</summary>
    public void Show(CompactTrailing trailing)
    {
        bool visible = trailing.Kind is TrailingKind.Ring or TrailingKind.Battery or TrailingKind.Equalizer;
        Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (!visible)
        {
            StopDancing();
            _shown = trailing;
            return;
        }

        bool equalizer = trailing.Kind == TrailingKind.Equalizer;
        _bars.Visibility = equalizer ? Visibility.Visible : Visibility.Collapsed;
        _track.Visibility = equalizer ? Visibility.Collapsed : Visibility.Visible;
        _fill.Visibility = equalizer ? Visibility.Collapsed : Visibility.Visible;

        if (equalizer)
        {
            StartDancing();
        }
        else
        {
            StopDancing();
            double sweep = trailing.Kind == TrailingKind.Battery ? BatterySweep : 360;
            double start = trailing.Kind == TrailingKind.Battery ? 135 : -90;

            if (_shown.Kind != trailing.Kind)
            {
                _track.Data = Arc(start, sweep);
            }

            _fill.Data = Arc(start, sweep * Math.Clamp(trailing.Value, 0, 1));
        }

        _shown = trailing;
    }

    /// <summary>Arc de cercle centré, en degrés, 0° à droite, sens horaire.</summary>
    private PathGeometry? Arc(double startDegrees, double sweepDegrees)
    {
        if (sweepDegrees <= 0.5)
        {
            return null;
        }

        double radius = (_side - Stroke) / 2;
        double center = _side / 2;

        // Un tour complet ne se dessine pas en un seul arc : deux demi-tours.
        sweepDegrees = Math.Min(sweepDegrees, 359.9);

        global::Windows.Foundation.Point At(double degrees)
        {
            double r = degrees * Math.PI / 180;
            return new global::Windows.Foundation.Point(center + (radius * Math.Cos(r)), center + (radius * Math.Sin(r)));
        }

        var figure = new PathFigure { StartPoint = At(startDegrees), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = At(startDegrees + sweepDegrees),
            Size = new global::Windows.Foundation.Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepDegrees > 180
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>Barres qui montent et descendent à des rythmes décalés.</summary>
    private void StartDancing()
    {
        if (_dancing)
        {
            return;
        }

        _dancing = true;

        if (!GlyphView.AnimationsEnabled)
        {
            // Animations réduites : des barres au repos, de hauteurs différentes.
            double[] still = [0.5, 1, 0.7];

            for (int i = 0; i < _barCells.Length; i++)
            {
                ElementCompositionPreview.GetElementVisual(_barCells[i]).Scale = new Vector3(1, (float)still[i], 1);
            }

            return;
        }

        try
        {
            double[] periods = [0.9, 0.7, 1.1];

            for (int i = 0; i < _barCells.Length; i++)
            {
                Visual visual = ElementCompositionPreview.GetElementVisual(_barCells[i]);
                visual.CenterPoint = new Vector3(1, 6, 0);
                Compositor compositor = visual.Compositor;

                Vector3KeyFrameAnimation dance = compositor.CreateVector3KeyFrameAnimation();
                dance.InsertKeyFrame(0f, new Vector3(1, 0.35f, 1));
                dance.InsertKeyFrame(0.5f, new Vector3(1, 1f, 1));
                dance.InsertKeyFrame(1f, new Vector3(1, 0.35f, 1));
                dance.Duration = TimeSpan.FromSeconds(periods[i]);
                dance.IterationBehavior = AnimationIterationBehavior.Forever;
                visual.StartAnimation("Scale", dance);
            }
        }
        catch (Exception)
        {
            // Compositeur indisponible : des barres immobiles valent mieux qu'une chute.
        }
    }

    private void StopDancing()
    {
        if (!_dancing)
        {
            return;
        }

        _dancing = false;

        try
        {
            foreach (Rectangle bar in _barCells)
            {
                Visual visual = ElementCompositionPreview.GetElementVisual(bar);
                visual.StopAnimation("Scale");
                visual.Scale = Vector3.One;
            }
        }
        catch (Exception)
        {
            // Rien à arrêter.
        }
    }
}
