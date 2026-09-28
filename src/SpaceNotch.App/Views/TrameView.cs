using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Motion;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Audio;
using Color = Windows.UI.Color;

namespace SpaceNotch_App.Views;

/// <summary>
/// La trame des scènes ouvertes (<see cref="TrameField"/>, choix B3) : un seul
/// tracé fait d'un carré par pixel allumé, calé sur les pixels physiques de
/// l'écran, posé entre la surface noire et le contenu.
///
/// <para>
/// Un tracé plutôt qu'une image modifiable (<c>WriteableBitmap</c>) : l'image
/// était remplie (le journal le montrait) mais restait invisible à l'écran au
/// tournage ; un tracé XAML s'affiche, net, sans interopérabilité.
/// </para>
///
/// <para>
/// Elle n'est calculée qu'une fois la forme posée : pendant que le ressort
/// ouvre la notch, elle s'efface, puis revient en fondu à la bonne taille.
/// Dans le lecteur, elle suit le niveau crête de la sortie audio, lu dix fois
/// par seconde, sans jamais enregistrer le son.
/// </para>
/// </summary>
public sealed partial class TrameView : Grid
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan MusicTick = TimeSpan.FromMilliseconds(100);

    private readonly Microsoft.UI.Xaml.Shapes.Path _path = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly SolidColorBrush _brush = new();
    private DispatcherQueueTimer? _settle;
    private DispatcherQueueTimer? _music;
    private AudioPeakMeter? _meter;
    private int _logged = -1;
    private double _width, _height, _radius, _shoulder;
    private double _level;
    private Color? _tint;
    private FrameworkElement? _scene;
    private bool _musicOn;
    private bool _allowedShown = true;

    public TrameView()
    {
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Opacity = 0;
        _path.Fill = _brush;
        Children.Add(_path);
        Unloaded += (_, _) => StopMusic();
    }

    /// <summary>Faux en contraste élevé, en thème clair ou sans teinte : aucune trame.</summary>
    public bool IsAllowed { get; set; } = true;

    /// <summary>Faux quand Windows réduit les animations : la trame reste fixe.</summary>
    public bool Animate { get; set; } = true;

    /// <summary>
    /// Montre la trame pour une scène ouverte, ou la retire (<paramref name="scene"/> nul).
    /// </summary>
    public void Present(FrameworkElement? scene, Color? tint, bool music)
    {
        // Une scène qui se redessine souvent (une progression, une position de
        // lecture) ne doit pas relancer la trame à chaque fois.
        bool same = ReferenceEquals(scene, _scene) && Nullable.Equals(tint, _tint) && music == _musicOn && _allowedShown == IsAllowed;
        _allowedShown = IsAllowed;

        if (same && (scene is null || Opacity > 0 || (_settle?.IsRunning ?? false)))
        {
            return;
        }

        _scene = scene;
        _tint = tint;

        bool show = IsAllowed && scene is not null && tint is not null && _height >= TrameField.MinimumHeightDip;

        if (!show)
        {
            Opacity = 0;
            StopMusic();
            return;
        }

        if (music && Animate)
        {
            StartMusic();
        }
        else
        {
            StopMusic();
            _level = 0;
        }

        Schedule();
    }

    /// <summary>
    /// La forme a changé. La trame s'efface et sera recalculée quand le ressort
    /// aura fini de bouger.
    /// </summary>
    public void Resize(double width, double height, double radius, double shoulder)
    {
        if (Math.Abs(width - _width) < 0.5 && Math.Abs(height - _height) < 0.5 && Math.Abs(radius - _radius) < 0.5)
        {
            return;
        }

        _width = width;
        _height = height;
        _radius = radius;
        _shoulder = shoulder;
        Width = width;
        Height = height;
        Opacity = 0;
        Schedule();
    }

    private (double X, double Y)? _spot;
    private long _lastSpotDraw;

    /// <summary>
    /// Projecteur tramé (A4) : la trame s'éclaire autour de ce point (DIP,
    /// repère de la notch), ou s'éteint là où elle n'a rien à dire avec
    /// <c>null</c>. Redessinée au plus trente fois par seconde, seulement
    /// quand la trame est déjà visible.
    /// </summary>
    public void Spotlight((double X, double Y)? at)
    {
        if (_scene is null || _tint is null || (at is null && _spot is null))
        {
            return;
        }

        _spot = at;
        long now = Environment.TickCount64;

        if (at is not null && now - _lastSpotDraw < 33)
        {
            return;
        }

        _lastSpotDraw = now;

        try
        {
            Draw();
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[TRAME] projecteur impossible", ex);
        }
    }

    private void Schedule()
    {
        _settle ??= CreateTimer(SettleDelay, () =>
        {
            _settle!.Stop();

            try
            {
                Draw();
                FadeIn();
            }
            catch (Exception ex)
            {
                // Une trame qui échoue laisse le noir : c'est le repli correct, et il se journalise.
                MiniLogger.Log("[TRAME] dessin impossible", ex);
            }
        });

        _settle.Stop();
        _settle.Start();
    }

    private DispatcherQueueTimer CreateTimer(TimeSpan interval, Action tick)
    {
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = interval;
        timer.Tick += (_, _) => tick();
        return timer;
    }

    private void StartMusic()
    {
        if (_musicOn)
        {
            return;
        }

        _musicOn = true;
        _meter ??= new AudioPeakMeter();
        _music ??= CreateTimer(MusicTick, () =>
        {
            _level = TrameField.Smooth(_level, _meter?.Read() ?? 0);

            if (_settle is null || !_settle.IsRunning)
            {
                try
                {
                    Draw();
                }
                catch (Exception ex)
                {
                    StopMusic();
                    MiniLogger.Log("[TRAME] dessin impossible", ex);
                }
            }
        });

        _music.Start();
    }

    private void StopMusic()
    {
        _musicOn = false;
        _music?.Stop();
    }

    private void FadeIn()
    {
        if (_scene is null || _tint is null || !IsAllowed)
        {
            return;
        }

        Opacity = TrameField.Opacity;
    }

    /// <summary>Calcule l'image : cellules allumées, en pixels physiques, hors du contenu.</summary>
    private void Draw()
    {
        if (_scene is null || _tint is not { } tint || XamlRoot is null || _height < TrameField.MinimumHeightDip)
        {
            return;
        }

        double scale = XamlRoot.RasterizationScale;
        int widthPx = (int)Math.Round(_width * scale), heightPx = (int)Math.Round(_height * scale);

        if (widthPx <= 0 || heightPx <= 0)
        {
            return;
        }

        double cell = Math.Max(2, Math.Round(TrameField.CellDip * scale));
        List<(int Column, int Row)> cells = TrameField.Cells(
            widthPx,
            heightPx,
            cell,
            _radius * scale,
            _shoulder * scale,
            Obstacles(scale),
            _musicOn ? _level : null,
            _spot is { } spot ? (spot.X * scale, spot.Y * scale) : null,
            TrameField.SpotRadiusDip * scale);

        // Un pixel d'écran d'écart entre deux cellules : la trame se lit en pixels, pas en aplat.
        double size = (cell - (cell >= 3 ? 1 : 0)) / scale;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };

        foreach ((int column, int row) in cells)
        {
            group.Children.Add(new RectangleGeometry
            {
                Rect = new global::Windows.Foundation.Rect(column * cell / scale, row * cell / scale, size, size)
            });
        }

        _brush.Color = tint;
        _path.Data = group;

        // Une ligne par scène dans le journal : assez pour diagnostiquer, sans le noyer.
        int signature = HashCode.Combine(widthPx, heightPx, _scene.GetHashCode());

        if (signature != _logged)
        {
            _logged = signature;
            MiniLogger.Log($"[TRAME] {widthPx}×{heightPx} px, cellule {cell}, {cells.Count} pixels allumés");
        }
    }

    /// <summary>Tout ce qui se lit dans la scène garde sa marge noire.</summary>
    private List<TrameRect> Obstacles(double scale)
    {
        var rects = new List<TrameRect>();

        if (_scene is null)
        {
            return rects;
        }

        double clearance = TrameField.ClearanceDip * scale;
        Collect(_scene, rects, scale, clearance);
        return rects;
    }

    private void Collect(DependencyObject node, List<TrameRect> rects, double scale, double clearance)
    {
        int count = VisualTreeHelper.GetChildrenCount(node);

        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is not FrameworkElement child
                || child.Visibility != Visibility.Visible
                || child.Opacity <= 0.01
                || child.ActualWidth <= 0
                || child.ActualHeight <= 0)
            {
                continue;
            }

            if (IsContent(child))
            {
                try
                {
                    var bounds = child.TransformToVisual(this).TransformBounds(new global::Windows.Foundation.Rect(0, 0, child.ActualWidth, child.ActualHeight));
                    rects.Add(new TrameRect(bounds.Left * scale, bounds.Top * scale, bounds.Right * scale, bounds.Bottom * scale).Inflate(clearance));
                }
                catch (ArgumentException)
                {
                    // Élément pas encore dans l'arbre : il sera pris au prochain calcul.
                }

                continue;
            }

            Collect(child, rects, scale, clearance);
        }
    }

    /// <summary>
    /// Ce qui se lit : texte, image, contrôle, dessin marqué, ou un fond coloré (la piste d'une
    /// barre de volume). Un panneau coloré qui couvre presque toute la scène
    /// n'est qu'un fond, pas un contenu.
    /// </summary>
    private bool IsContent(FrameworkElement element) => element switch
    {
        // Un dessin qui se lit sans être du texte — la courbe du moniteur — le
        // déclare par son étiquette.
        { Tag: "trame-avoid" } => true,
        TextBlock or Image or ButtonBase or Slider or TextBox or FontIcon or GlyphView or TrailingView => true,
        Border { Background: SolidColorBrush { Color.A: > 0 } } => true,
        Panel { Background: SolidColorBrush { Color.A: > 0 } } panel => panel.ActualHeight < _height * 0.6,
        _ => false
    };
}
