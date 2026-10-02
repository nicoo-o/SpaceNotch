using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Views;

/// <summary>
/// Pixel (P1) : les deux yeux de la notch au repos. La vue ne décide rien :
/// l'humeur, le regard et le moment des clignements viennent de
/// <see cref="PixelGaze"/>, par la fenêtre. Elle se contente de les dessiner,
/// à ressort, dans la teinte du logo.
///
/// <para>
/// Vague 7 : une forme peut être imposée (fatigue, soir, bâillement), les yeux
/// peuvent se croiser (hors ligne), une goutte de sueur peut glisser (chaleur),
/// la teinte suit l'heure, et les yeux battent la mesure de la musique.
/// </para>
/// </summary>
public sealed partial class PixelEyesView : Grid
{
    private readonly SolidColorBrush _ink = new(Microsoft.UI.ColorHelper.FromArgb(255, 0x7F, 0xE6, 0xFF));

    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly Grid _leftCell = NewCell();
    private readonly Grid _rightCell = NewCell();
    private readonly Border _left;
    private readonly Border _right;
    private readonly Border[] _crosses = new Border[4];
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Border _drop;

    private PixelMood _mood = PixelMood.Awake;
    private EyeShape? _override;
    private bool _blinking;
    private bool _crossed;
    private double _beatY;
    private double _squash;
    private Vector3 _look;

    public PixelEyesView()
    {
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        Height = 12;

        _left = NewEye(_ink);
        _right = NewEye(_ink);
        _leftCell.Children.Add(_left);
        _rightCell.Children.Add(_right);

        for (int i = 0; i < _crosses.Length; i++)
        {
            // Deux traits de 2 × 10 en croix par œil, cachés tant que le réseau est là.
            _crosses[i] = new Border
            {
                Width = 2,
                Height = 10,
                CornerRadius = new CornerRadius(1),
                Background = _ink,
                RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new RotateTransform { Angle = i % 2 == 0 ? 45 : -45 },
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            (i < 2 ? _leftCell : _rightCell).Children.Add(_crosses[i]);
        }

        _row.Children.Add(_leftCell);
        _row.Children.Add(_rightCell);
        Children.Add(_row);

        _drop = new Border { Width = 2, Height = 3, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x8F, 0xD0, 0xFF)), Opacity = 0 };
        _overlay.Children.Add(_drop);
        Children.Add(_overlay);

        ApplyShape(PixelGaze.Shape(_mood));
        Loaded += (_, _) => EnableSprings();
    }

    /// <summary>Faux quand Windows réduit les animations : le regard se pose sans ressort.</summary>
    public bool Animate { get; set; } = true;

    public PixelMood Mood => _mood;

    public void SetMood(PixelMood mood)
    {
        if (mood == _mood)
        {
            return;
        }

        _mood = mood;

        if (!_blinking)
        {
            ApplyShape(BaseShape);
        }

        if (mood == PixelMood.Asleep)
        {
            Look(0, PixelGaze.MaxLookY);
        }
    }

    /// <summary>Forme imposée par la fenêtre (fatigue, soir, bâillement) ; null rend la forme de l'humeur.</summary>
    public void SetOverride(EyeShape? shape)
    {
        if (shape == _override)
        {
            return;
        }

        _override = shape;

        if (!_blinking)
        {
            ApplyShape(BaseShape);
        }
    }

    /// <summary>Yeux en croix (plus de réseau).</summary>
    public void SetCrossed(bool crossed)
    {
        if (crossed == _crossed)
        {
            return;
        }

        _crossed = crossed;
        _left.Visibility = _right.Visibility = crossed ? Visibility.Collapsed : Visibility.Visible;

        foreach (Border cross in _crosses)
        {
            cross.Visibility = crossed ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Teinte des yeux : le cyan le jour, l'ambre le soir.</summary>
    public void SetTint(global::Windows.UI.Color color)
    {
        if (_ink.Color != color)
        {
            _ink.Color = color;
        }
    }

    /// <summary>Le pas de danse : décalage vertical (DIP) et écrasement (0..1).</summary>
    public void SetBeat(double offset, double squash)
    {
        if (Math.Abs(offset - _beatY) < 0.05 && Math.Abs(squash - _squash) < 0.02)
        {
            return;
        }

        _beatY = offset;
        _squash = squash;
        ApplyTranslation();

        if (!_blinking)
        {
            ApplyShape(BaseShape);
        }
    }

    /// <summary>Tourne les yeux d'un décalage en DIP (voir <see cref="PixelGaze.Look"/>).</summary>
    public void Look(double x, double y)
    {
        _look = new Vector3((float)x, (float)y, 0);
        ApplyTranslation();
    }

    private void ApplyTranslation()
    {
        var offset = _look + new Vector3(0, (float)_beatY, 0);
        _leftCell.Translation = offset;
        _rightCell.Translation = offset;
    }

    /// <summary>
    /// Centre de chaque œil dans le repère de <paramref name="relativeTo"/>,
    /// regard compris : les transitions partent de là où ils sont vraiment.
    /// </summary>
    public (global::Windows.Foundation.Point Left, global::Windows.Foundation.Point Right) Centers(UIElement relativeTo)
    {
        global::Windows.Foundation.Point Center(Grid cell)
        {
            global::Windows.Foundation.Point p = cell.TransformToVisual(relativeTo).TransformPoint(new global::Windows.Foundation.Point(cell.ActualWidth / 2, cell.ActualHeight / 2));
            return new global::Windows.Foundation.Point(p.X + cell.Translation.X, p.Y + cell.Translation.Y);
        }

        return (Center(_leftCell), Center(_rightCell));
    }

    /// <summary>Forme actuelle d'un œil, clignement compris.</summary>
    public EyeShape CurrentShape => _blinking ? PixelGaze.Blink : BaseShape;

    private EyeShape BaseShape => PixelVitals.Squashed(_override ?? PixelGaze.Shape(_mood), _squash);

    /// <summary>Un clignement : les yeux se ferment puis se rouvrent.</summary>
    public void Blink()
    {
        if (_blinking || _mood == PixelMood.Asleep || _crossed)
        {
            return;
        }

        _blinking = true;
        ApplyShape(PixelGaze.Blink);

        _reopen ??= CreateReopenTimer();
        _reopen.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _reopen;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateReopenTimer()
    {
        Microsoft.UI.Dispatching.DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(PixelGaze.BlinkMilliseconds);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            _blinking = false;
            ApplyShape(BaseShape);
        };
        return timer;
    }

    // ---- La goutte de sueur -------------------------------------------------

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _sweatTimer;
    private DateTime _sweatStart;

    /// <summary>Une goutte glisse le long de l'œil droit, toutes les 2,4 s, tant qu'il fait chaud.</summary>
    public void SetSweating(bool sweating)
    {
        if (!sweating)
        {
            _sweatTimer?.Stop();
            _drop.Opacity = 0;
            return;
        }

        if (_sweatTimer is { IsRunning: true })
        {
            return;
        }

        _sweatStart = DateTime.UtcNow;
        _sweatTimer ??= CreateSweatTimer();
        _sweatTimer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateSweatTimer()
    {
        Microsoft.UI.Dispatching.DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(40);
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            double t = (DateTime.UtcNow - _sweatStart).TotalMilliseconds % 2400 / 1000;
            double x = _rightCell.ActualOffset.X + _rightCell.ActualWidth + 3 + _look.X;
            Canvas.SetLeft(_drop, x);

            // Elle apparaît, glisse et s'efface en une seconde, puis attend.
            if (t < 1)
            {
                Canvas.SetTop(_drop, -4 + (t * t * 12));
                _drop.Opacity = t < 0.15 ? t / 0.15 : t > 0.85 ? (1 - t) / 0.15 : 1;
            }
            else
            {
                _drop.Opacity = 0;
            }
        };
        return timer;
    }

    private void ApplyShape(EyeShape shape)
    {
        // Les cases suivent la largeur des yeux : l'écart entre eux reste celui d'avant (7).
        _leftCell.Width = _rightCell.Width = shape.Width;

        foreach (Border eye in new[] { _left, _right })
        {
            eye.Width = shape.Width;
            eye.Height = shape.Height;
            eye.CornerRadius = new CornerRadius(Math.Min(shape.Width, shape.Height) / 2 * shape.Roundness);
        }
    }

    /// <summary>Le regard glisse vers sa nouvelle direction au lieu d'y sauter.</summary>
    private void EnableSprings()
    {
        if (!Animate)
        {
            return;
        }

        foreach (Grid cell in new[] { _leftCell, _rightCell })
        {
            cell.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(180) };
        }
    }

    private static Grid NewCell() => new() { Width = 8, Height = 12, IsHitTestVisible = false };

    private static Border NewEye(Brush ink) => new()
    {
        Background = ink,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        IsHitTestVisible = false
    };
}
