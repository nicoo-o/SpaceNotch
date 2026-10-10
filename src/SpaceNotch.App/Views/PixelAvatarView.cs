using System;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Views;

/// <summary>
/// Pixel, avatar des agents (ADR-029) : ses deux yeux et un petit signe, à la
/// place du glyphe et de la grille qui tourne. La vue ne fait que dessiner la
/// pose que donne <see cref="PixelAvatar"/> ; elle n'anime que tant qu'elle est
/// affichée, et quand Windows réduit les animations, Pixel reste posé.
///
/// <para>
/// Tout est créé une fois : à chaque image, seuls des décalages, des angles et
/// des opacités changent (aucune géométrie recréée, voir n° 46).
/// </para>
/// </summary>
public sealed partial class PixelAvatarView : Canvas
{
    /// <summary>Les yeux de Pixel au repos.</summary>
    private static readonly SolidColorBrush Ink = new(ColorHelper.FromArgb(0xFF, 0x7F, 0xE6, 0xFF));

    private static readonly SolidColorBrush Ask = new(ColorHelper.FromArgb(0xFF, 0x85, 0xB7, 0xEB));
    private static readonly SolidColorBrush Ok = new(ColorHelper.FromArgb(0xFF, 0x97, 0xC4, 0x59));
    private static readonly SolidColorBrush Alarm = new(ColorHelper.FromArgb(0xFF, 0xFF, 0x6B, 0x80));

    /// <summary>Côté d'un pixel des signes, en DIP à l'échelle 1.</summary>
    private const double Dot = 1.6;

    // « ? » et « ! » en 3 × 5, comme la police de Pixel.
    private static readonly string[] QuestionGlyph = ["###", "..#", ".#.", "...", ".#."];
    private static readonly string[] ExclamationGlyph = [".#.", ".#.", ".#.", "...", ".#."];

    private readonly Border _leftEye = NewEye();
    private readonly Border _rightEye = NewEye();
    private readonly Polyline _leftArc = NewArc();
    private readonly Polyline _rightArc = NewArc();
    private readonly RotateTransform _leftTilt = new();
    private readonly RotateTransform _rightTilt = new();
    private readonly Rectangle[] _dots = new Rectangle[3];
    private readonly Canvas _question = Glyph(QuestionGlyph, Ask);
    private readonly Canvas _exclamation = Glyph(ExclamationGlyph, Alarm);
    private readonly Canvas _sparkles = new();
    private readonly Canvas[] _sparkle = new Canvas[2];

    private DispatcherQueueTimer? _timer;
    private DateTime _start = DateTime.UtcNow;
    private double _scale = 1;
    private AgentMood _mood = AgentMood.Thinking;
    private bool _animate = true;
    private bool _suspended;

    public PixelAvatarView()
    {
        IsHitTestVisible = false;

        _leftEye.RenderTransform = _leftTilt;
        _rightEye.RenderTransform = _rightTilt;
        Children.Add(_leftEye);
        Children.Add(_rightEye);
        Children.Add(_leftArc);
        Children.Add(_rightArc);

        for (int i = 0; i < _dots.Length; i++)
        {
            _dots[i] = new Rectangle { Width = Dot * 1.25, Height = Dot * 1.25, Fill = Ink };
            SetLeft(_dots[i], SignLeft + (i * Dot * 2.2));
            SetTop(_dots[i], PixelAvatar.EyeY + 2.5);
            Children.Add(_dots[i]);
        }

        SetLeft(_question, SignLeft + 1);
        SetTop(_question, PixelAvatar.EyeY - (2.5 * Dot));
        SetLeft(_exclamation, SignLeft + 1);
        SetTop(_exclamation, PixelAvatar.EyeY - (2.5 * Dot));
        Children.Add(_question);
        Children.Add(_exclamation);

        // Deux étincelles en croix, qui brillent tour à tour.
        _sparkle[0] = Sparkle(SignLeft + 2.5, PixelAvatar.EyeY - 4);
        _sparkle[1] = Sparkle(SignLeft + 6.5, PixelAvatar.EyeY + 2.5);
        _sparkles.Children.Add(_sparkle[0]);
        _sparkles.Children.Add(_sparkle[1]);
        Children.Add(_sparkles);

        Width = PixelAvatar.Width;
        Height = PixelAvatar.Height;
        RenderTransform = new ScaleTransform();

        Draw();
        Loaded += (_, _) => Sync();
        Unloaded += (_, _) => _timer?.Stop();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Sync());
    }

    private static double SignLeft => PixelAvatar.EyeWidth + PixelAvatar.EyeGap + PixelAvatar.EyeWidth + PixelAvatar.SignGap;

    /// <summary>Taille de l'avatar : 1 vaut les yeux de Pixel au repos.</summary>
    public double AvatarScale
    {
        get => _scale;
        set
        {
            double scale = Math.Max(0.5, value);

            if (Math.Abs(scale - _scale) > 0.001)
            {
                _scale = scale;
                var transform = (ScaleTransform)RenderTransform;
                transform.ScaleX = scale;
                transform.ScaleY = scale;

                // La place occupée suit l'échelle : la ligne de la pastille se cale dessus.
                Margin = new Thickness(0, 0, PixelAvatar.Width * (scale - 1), PixelAvatar.Height * (scale - 1));
            }
        }
    }

    public AgentMood Mood
    {
        get => _mood;
        set
        {
            if (_mood != value)
            {
                // Une nouvelle humeur commence au début de son geste.
                _mood = value;
                _start = DateTime.UtcNow;
                Draw();
            }
        }
    }

    /// <summary>Faux quand Windows réduit les animations : Pixel reste posé.</summary>
    public bool Animate
    {
        get => _animate;
        set
        {
            _animate = value;
            Sync();
        }
    }

    /// <summary>Vrai tant que la notch est retirée : rien ne bat (n° 49).</summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended != value)
            {
                _suspended = value;
                Sync();
            }
        }
    }

    /// <summary>Centre d'un œil au repos, en DIP de la vue, échelle comprise.</summary>
    public (double X, double Y) EyeCenter(bool left)
        => ((left ? PixelAvatar.LeftEyeX : PixelAvatar.RightEyeX) * _scale, PixelAvatar.EyeY * _scale);

    private void Sync()
    {
        bool running = Visibility == Visibility.Visible && IsLoaded && _animate && !_suspended;

        if (!running)
        {
            _timer?.Stop();
            Draw();
            return;
        }

        if (_timer is null)
        {
            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / PixelAvatar.FramesPerSecond);
            _timer.IsRepeating = true;
            _timer.Tick += (_, _) => Draw();
        }

        _timer.Start();
    }

    private void Draw()
    {
        double seconds = (DateTime.UtcNow - _start).TotalSeconds;
        AvatarPose pose = PixelAvatar.Pose(_mood, seconds, _animate && !_suspended);

        PlaceEye(_leftEye, _leftArc, _leftTilt, PixelAvatar.LeftEyeX, pose, pose.Tilt);
        PlaceEye(_rightEye, _rightArc, _rightTilt, PixelAvatar.RightEyeX, pose, -pose.Tilt);

        for (int i = 0; i < _dots.Length; i++)
        {
            _dots[i].Opacity = pose.Sign == AvatarSign.Dots ? (i < pose.SignStep ? 1 : 0.25) : 0;
        }

        _question.Opacity = pose.Sign == AvatarSign.Question ? pose.SignOpacity : 0;
        _exclamation.Opacity = pose.Sign == AvatarSign.Exclamation ? pose.SignOpacity : 0;
        _sparkles.Opacity = pose.Sign == AvatarSign.Sparkle ? 1 : 0;
        _sparkle[0].Opacity = pose.SignStep == 0 ? 1 : 0.35;
        _sparkle[1].Opacity = pose.SignStep == 1 ? 1 : 0.35;

        // Le saut et le hochement emportent tout l'avatar, le signe compris.
        var offset = new Vector3((float)pose.Shake, (float)pose.Hop, 0);

        foreach (UIElement child in Children)
        {
            child.Translation = offset;
        }
    }

    private static void PlaceEye(Border eye, Polyline arc, RotateTransform tilt, double centerX, AvatarPose pose, double angle)
    {
        EyeShape shape = pose.Eye;
        double left = centerX - (shape.Width / 2) + pose.LookX;
        double top = PixelAvatar.EyeY - (shape.Height / 2) + pose.LookY;

        eye.Visibility = pose.Arcs ? Visibility.Collapsed : Visibility.Visible;
        arc.Visibility = pose.Arcs ? Visibility.Visible : Visibility.Collapsed;

        if (pose.Arcs)
        {
            // « ^ » : le dessin est fixe, seule sa place change.
            SetLeft(arc, left);
            SetTop(arc, top);
            return;
        }

        eye.Width = shape.Width;
        eye.Height = shape.Height;
        eye.CornerRadius = new CornerRadius(Math.Min(shape.Width, shape.Height) / 2 * shape.Roundness);
        tilt.CenterX = shape.Width / 2;
        tilt.CenterY = shape.Height / 2;
        tilt.Angle = angle;
        SetLeft(eye, left);
        SetTop(eye, top);
    }

    private static Border NewEye() => new() { Background = Ink };

    private static Polyline NewArc()
    {
        EyeShape shape = PixelAvatar.Laughing;
        var arc = new Polyline
        {
            Stroke = Ink,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Visibility = Visibility.Collapsed
        };
        arc.Points.Add(new global::Windows.Foundation.Point(1, shape.Height));
        arc.Points.Add(new global::Windows.Foundation.Point(shape.Width / 2, 0.5));
        arc.Points.Add(new global::Windows.Foundation.Point(shape.Width - 1, shape.Height));
        return arc;
    }

    private static Canvas Glyph(string[] rows, Brush brush)
    {
        var glyph = new Canvas { Opacity = 0 };

        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < rows[y].Length; x++)
            {
                if (rows[y][x] != '#')
                {
                    continue;
                }

                var pixel = new Rectangle { Width = Dot + 0.2, Height = Dot + 0.2, Fill = brush };
                SetLeft(pixel, x * Dot);
                SetTop(pixel, y * Dot);
                glyph.Children.Add(pixel);
            }
        }

        return glyph;
    }

    private static Canvas Sparkle(double x, double y)
    {
        var sparkle = new Canvas();
        sparkle.Children.Add(new Rectangle { Width = 1.2, Height = 4.4, Fill = Ok });
        sparkle.Children.Add(new Rectangle { Width = 4.4, Height = 1.2, Fill = Ok });
        SetLeft(sparkle.Children[0], 1.6);
        SetTop(sparkle.Children[1], 1.6);
        SetLeft(sparkle, x - 2.2);
        SetTop(sparkle, y - 2.2);
        return sparkle;
    }
}
