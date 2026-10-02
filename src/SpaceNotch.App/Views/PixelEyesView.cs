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
/// à ressort, dans la teinte cyan du logo.
/// </summary>
public sealed partial class PixelEyesView : StackPanel
{
    private static readonly SolidColorBrush Cyan = new(Microsoft.UI.ColorHelper.FromArgb(255, 0x7F, 0xE6, 0xFF));

    private readonly Border _left = NewEye();
    private readonly Border _right = NewEye();
    private PixelMood _mood = PixelMood.Awake;
    private bool _blinking;

    public PixelEyesView()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 7;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        Height = 12;
        Children.Add(_left);
        Children.Add(_right);
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
            ApplyShape(PixelGaze.Shape(mood));
        }

        if (mood == PixelMood.Asleep)
        {
            Look(0, PixelGaze.MaxLookY);
        }
    }

    /// <summary>Tourne les yeux d'un décalage en DIP (voir <see cref="PixelGaze.Look"/>).</summary>
    public void Look(double x, double y)
    {
        var offset = new Vector3((float)x, (float)y, 0);
        _left.Translation = offset;
        _right.Translation = offset;
    }

    /// <summary>
    /// Centre de chaque œil dans le repère de <paramref name="relativeTo"/>,
    /// regard compris : la transition du repos part de là où ils sont vraiment.
    /// </summary>
    public (global::Windows.Foundation.Point Left, global::Windows.Foundation.Point Right) Centers(UIElement relativeTo)
    {
        global::Windows.Foundation.Point Center(Border eye)
        {
            global::Windows.Foundation.Point p = eye.TransformToVisual(relativeTo).TransformPoint(new global::Windows.Foundation.Point(eye.ActualWidth / 2, eye.ActualHeight / 2));
            return new global::Windows.Foundation.Point(p.X + eye.Translation.X, p.Y + eye.Translation.Y);
        }

        return (Center(_left), Center(_right));
    }

    /// <summary>Forme actuelle d'un œil, clignement compris.</summary>
    public EyeShape CurrentShape => _blinking ? PixelGaze.Blink : PixelGaze.Shape(_mood);

    /// <summary>Un clignement : les yeux se ferment puis se rouvrent.</summary>
    public void Blink()
    {
        if (_blinking || _mood == PixelMood.Asleep)
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
            ApplyShape(PixelGaze.Shape(_mood));
        };
        return timer;
    }

    private void ApplyShape(EyeShape shape)
    {
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

        foreach (Border eye in new[] { _left, _right })
        {
            eye.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(180) };
        }
    }

    private static Border NewEye() => new()
    {
        Background = Cyan,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false
    };
}
