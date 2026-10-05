using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using Color = Windows.UI.Color;
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
    private readonly Canvas _spinner = new() { Width = DefaultSide, Height = DefaultSide, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Rectangle[] _spinnerPixels = new Rectangle[SpinnerCheck.Count];
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _spinTimer;
    private DateTime _spinStart;
    private DateTime _morphStart;
    private double _frozenAt;
    private bool _morphing;
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

        for (int i = 0; i < _spinnerPixels.Length; i++)
        {
            _spinnerPixels[i] = new Rectangle { Width = SpinnerPixel, Height = SpinnerPixel, RadiusX = 0.4, RadiusY = 0.4 };
            _spinner.Children.Add(_spinnerPixels[i]);
        }

        _spinner.Visibility = Visibility.Collapsed;
        Children.Add(_spinner);
        Unloaded += (_, _) =>
        {
            StopDancing();
            RunSpin(false);
        };
    }

    /// <summary>Teinte de l'activité : la partie pleine de l'anneau, les barres.</summary>
    public Brush? Tint
    {
        get => _tint;
        set
        {
            _tint = value;
            _fill.Stroke = value;

            if (!_morphing && _shown.Kind != TrailingKind.Check)
            {
                foreach (Rectangle pixel in _spinnerPixels)
                {
                    pixel.Fill = value;
                }
            }

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
        bool visible = trailing.Kind is TrailingKind.Ring or TrailingKind.Battery or TrailingKind.Equalizer or TrailingKind.Spinner or TrailingKind.Check;
        Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (!visible)
        {
            StopDancing();
            RunSpin(false);
            _shown = trailing;
            return;
        }

        bool equalizer = trailing.Kind == TrailingKind.Equalizer;
        bool pixels = trailing.Kind is TrailingKind.Spinner or TrailingKind.Check;
        _bars.Visibility = equalizer ? Visibility.Visible : Visibility.Collapsed;
        _track.Visibility = equalizer || pixels ? Visibility.Collapsed : Visibility.Visible;
        _fill.Visibility = equalizer || pixels ? Visibility.Collapsed : Visibility.Visible;
        _spinner.Visibility = pixels ? Visibility.Visible : Visibility.Collapsed;

        if (pixels)
        {
            StopDancing();

            // Une coche republiée ne rejoue pas la migration.
            if (!(trailing.Kind == TrailingKind.Check && _shown.Kind == TrailingKind.Check))
            {
                ShowSpinner(trailing.Kind == TrailingKind.Check);
            }
        }
        else if (equalizer)
        {
            RunSpin(false);
            StartDancing();
        }
        else
        {
            RunSpin(false);
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

    /// <summary>Côté d'un pixel du spinner, en DIPs.</summary>
    private const double SpinnerPixel = 2;

    private static readonly Color CheckGreen = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x5F, 0xE0, 0x8A);

    /// <summary>
    /// Spinner qui devient une coche (M2) : les pixels tournent ; à la fin, ils
    /// glissent en coche (380 ms) puis s'allument en vert. Un minuteur ne tourne
    /// que tant que les pixels bougent.
    /// </summary>
    private void ShowSpinner(bool done)
    {
        bool wasSpinning = _shown.Kind == TrailingKind.Spinner;
        _spinTimer ??= CreateSpinTimer();

        if (!done)
        {
            _morphing = false;

            if (!wasSpinning)
            {
                _spinStart = DateTime.UtcNow;
            }

            if (GlyphView.AnimationsEnabled)
            {
                RunSpin(true);
            }

            PlaceSpinner(SpinnerCheck.Ring((DateTime.UtcNow - _spinStart).TotalSeconds), _tint);
            return;
        }

        // Fin sans avoir tourné, ou animations réduites : la coche d'emblée.
        if (!wasSpinning || !GlyphView.AnimationsEnabled)
        {
            RunSpin(false);
            _morphing = false;
            PlaceSpinner(SpinnerCheck.Done(), new SolidColorBrush(CheckGreen));
            return;
        }

        _frozenAt = (DateTime.UtcNow - _spinStart).TotalSeconds;
        _morphStart = DateTime.UtcNow;
        _morphing = true;
        RunSpin(true);
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateSpinTimer()
    {
        Microsoft.UI.Dispatching.DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += (_, _) =>
        {
            if (_morphing)
            {
                double p = (DateTime.UtcNow - _morphStart).TotalSeconds / SpinnerCheck.MorphSeconds;

                if (p >= 1)
                {
                    _morphing = false;
                    RunSpin(false);
                    PlaceSpinner(SpinnerCheck.Done(), new SolidColorBrush(CheckGreen));
                    return;
                }

                PlaceSpinner(SpinnerCheck.Morph(_frozenAt, p), _tint);
                return;
            }

            PlaceSpinner(SpinnerCheck.Ring((DateTime.UtcNow - _spinStart).TotalSeconds), _tint);
        };

        return timer;
    }

    private void PlaceSpinner(IReadOnlyList<(double X, double Y)> points, Brush? fill)
    {
        // Grille 7 × 7 posée dans le carré de l'élément, centrée.
        double step = (_side - SpinnerPixel) / 7.0;
        _spinner.Width = _side;
        _spinner.Height = _side;

        for (int i = 0; i < _spinnerPixels.Length && i < points.Count; i++)
        {
            Rectangle pixel = _spinnerPixels[i];
            pixel.Fill = fill;
            Canvas.SetLeft(pixel, Math.Round(points[i].X * step * 2) / 2);
            Canvas.SetTop(pixel, Math.Round(points[i].Y * step * 2) / 2);
        }
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

        // Égaliseur branché au vrai son (F3) : le niveau crête de la sortie
        // audio, lu vingt fois par seconde tant que les barres sont visibles.
        // En pause, le son se tait et les barres se posent.
        try
        {
            foreach (Rectangle bar in _barCells)
            {
                ElementCompositionPreview.GetElementVisual(bar).CenterPoint = new Vector3(1, 6, 0);
            }

            _meter ??= SpaceNotch.Platform.Windows.Audio.AudioPeakMeter.Shared;
            _danceStart = DateTime.UtcNow;

            if (_danceTimer is null)
            {
                _danceTimer = DispatcherQueue.CreateTimer();
                _danceTimer.Interval = TimeSpan.FromMilliseconds(50);
                _danceTimer.Tick += (_, _) => Dance();
            }

            RunDance(true);
        }
        catch (Exception)
        {
            // Compositeur ou mesure indisponible : des barres immobiles valent mieux qu'une chute.
        }
    }

    /// <summary>
    /// La notch est retirée (application en plein écran) : la toupie et
    /// l'égaliseur s'arrêtent, puis reprennent là où ils en étaient.
    /// </summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value)
            {
                return;
            }

            _suspended = value;

            // Ce qui est voulu, et non ce qui tournait : pendant le retrait, le
            // rendu peut redemander la toupie (il la relançait, notch cachée),
            // ou une coche peut remplacer la toupie (elle repartait au retour).
            RunSpin(_spinWanted);
            RunDance(_danceWanted);
        }
    }

    private bool _suspended;
    private bool _spinWanted;
    private bool _danceWanted;

    /// <summary>Démarre ou arrête la toupie, jamais pendant le retrait.</summary>
    private void RunSpin(bool on)
    {
        _spinWanted = on;

        if (on && !_suspended)
        {
            _spinTimer?.Start();
        }
        else
        {
            _spinTimer?.Stop();
        }
    }

    /// <summary>Démarre ou arrête l'égaliseur, jamais pendant le retrait.</summary>
    private void RunDance(bool on)
    {
        _danceWanted = on;

        if (on && !_suspended)
        {
            _danceTimer?.Start();
        }
        else
        {
            _danceTimer?.Stop();
        }
    }

    private SpaceNotch.Platform.Windows.Audio.AudioPeakMeter? _meter;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _danceTimer;
    private DateTime _danceStart;
    private double _level;

    private void Dance()
    {
        _level = TrameField.Smooth(_level, _meter?.Read() ?? 0);
        double[] heights = EqualizerBars.Heights(_level, (DateTime.UtcNow - _danceStart).TotalSeconds);

        for (int i = 0; i < _barCells.Length && i < heights.Length; i++)
        {
            ElementCompositionPreview.GetElementVisual(_barCells[i]).Scale = new Vector3(1, (float)heights[i], 1);
        }
    }

    private void StopDancing()
    {
        if (!_dancing)
        {
            return;
        }

        _dancing = false;
        RunDance(false);
        _level = 0;

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
