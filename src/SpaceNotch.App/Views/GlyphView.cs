using System;
using System.Collections.Generic;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Views;

/// <summary>
/// L'icône d'une activité, dans la matière de la notch : un motif de 7 × 7
/// pixels (<see cref="PixelGlyphs"/>) dont les pixels éteints restent en
/// filigrane et dont les pixels allumés s'éclairent du centre vers les bords
/// quand l'icône change.
///
/// <para>
/// Une clé sans motif — un greffon, un caractère isolé — garde son glyphe de
/// police : la même place, la même teinte.
/// </para>
///
/// <para>
/// Les pixels sont calés sur les pixels physiques de l'écran : à 100 %, un
/// pixel de l'icône vaut deux pixels de l'écran, sans écart ; dès que la place
/// le permet, un pixel d'écran les sépare. Jamais de demi-pixel, donc jamais de
/// bord flou.
/// </para>
/// </summary>
public sealed partial class GlyphView : Grid
{
    /// <summary>Opacité des pixels éteints : la grille se devine, sans se lire.</summary>
    private const double UnlitOpacity = 0.09;

    /// <summary>Faux lorsque Windows réduit les animations : l'icône s'allume d'un coup.</summary>
    public static bool AnimationsEnabled { get; set; } = true;

    private readonly FontIcon _font = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Canvas _pixels = new() { IsHitTestVisible = false };
    private readonly Rectangle[] _cells = new Rectangle[PixelGlyphs.Size * PixelGlyphs.Size];
    private IReadOnlyList<bool>? _mask;
    private string? _key;
    private string _fallbackKey = GlyphCatalog.FallbackKey;

    public GlyphView()
    {
        IsHitTestVisible = false;
        Children.Add(_font);
        Children.Add(_pixels);

        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new Rectangle();
            _pixels.Children.Add(_cells[i]);
        }

        ApplySize();
        Loaded += (_, _) => Layout();
        SizeChanged += (_, _) => Layout();
    }

    /// <summary>Côté de l'icône, en DIPs. Propriété de dépendance : le XAML la pose depuis un jeton.</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(GlyphView), new PropertyMetadata(16.0, (d, _) => ((GlyphView)d).ApplySize()));

    /// <summary>Côté de l'icône, en DIPs.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Clé utilisée quand <see cref="Key"/> est vide ou inconnue du glyphe de police.</summary>
    public string FallbackKey
    {
        get => _fallbackKey;
        set => _fallbackKey = value;
    }

    /// <summary>Teinte des pixels allumés. Propriété de dépendance : le XAML la pose depuis une ressource de thème.</summary>
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint), typeof(Brush), typeof(GlyphView), new PropertyMetadata(null, (d, _) => ((GlyphView)d).ApplyTint()));

    /// <summary>Teinte des pixels allumés — et du glyphe de police de repli.</summary>
    public Brush? Tint
    {
        get => (Brush?)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <summary>
    /// Clé d'icône de l'activité (« Music », « Bluetooth »…). Changer de clé
    /// rallume l'icône ; la même clé ne rejoue rien.
    /// </summary>
    public string? Key
    {
        get => _key;
        set
        {
            if (string.Equals(_key, value, StringComparison.Ordinal))
            {
                return;
            }

            _key = value;
            IReadOnlyList<bool>? previous = _mask;
            _mask = PixelGlyphs.Resolve(value) ?? (string.IsNullOrEmpty(value) ? PixelGlyphs.Resolve(_fallbackKey) : null);

            bool pixels = _mask is not null;
            _pixels.Visibility = pixels ? Visibility.Visible : Visibility.Collapsed;
            _font.Visibility = pixels ? Visibility.Collapsed : Visibility.Visible;

            if (!pixels)
            {
                _font.Glyph = GlyphCatalog.Resolve(value, _fallbackKey);
                return;
            }

            Layout();

            // Une icône qui en remplace une autre fait voyager ses pixels (P6) ;
            // une icône qui apparaît s'allume du centre vers les bords.
            if (previous is null || !Migrate(previous, _mask!))
            {
                LightUp();
            }
        }
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _frameTimer;
    private bool _visualDriven;
    private IReadOnlyList<bool[]>? _frames;
    private int _frame;

    /// <summary>
    /// Joue une petite scène en pixels (F1, F12) : les images défilent, puis
    /// l'icône de la clé courante revient. Rien quand Windows réduit les
    /// animations : l'icône reste simplement posée.
    /// </summary>
    public void Play(IReadOnlyList<bool[]> frames, int frameMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(frames);

        if (!AnimationsEnabled || frames.Count == 0 || _mask is null)
        {
            return;
        }

        _frames = frames;
        _frame = 0;
        _visualDriven = true;

        if (_frameTimer is null)
        {
            _frameTimer = DispatcherQueue.CreateTimer();
            _frameTimer.Tick += (_, _) => NextFrame();
        }

        _frameTimer.Interval = TimeSpan.FromMilliseconds(frameMilliseconds);
        ShowMask(frames[0]);
        _frameTimer.Start();
    }

    private void NextFrame()
    {
        _frame++;

        if (_frames is null || _frame >= _frames.Count)
        {
            _frameTimer?.Stop();
            _frames = null;

            if (_mask is not null)
            {
                ShowMask(_mask);
            }

            return;
        }

        ShowMask(_frames[_frame]);
    }

    private void ShowMask(IReadOnlyList<bool> mask)
    {
        for (int i = 0; i < _cells.Length && i < mask.Count; i++)
        {
            // Une fois l'opacité du visuel écrite, XAML ne la pilote plus : la
            // case n'obéit plus qu'au visuel. La scène écrit donc là, et la mise
            // en page suivante aussi (_visualDriven).
            Visual visual = ElementCompositionPreview.GetElementVisual(_cells[i]);
            visual.StopAnimation("Opacity");
            float opacity = mask[i] ? 1f : (float)UnlitOpacity;
            _cells[i].Opacity = opacity;
            visual.Opacity = opacity;
        }
    }

    private void ApplyTint()
    {
        Brush? tint = Tint;
        _font.Foreground = tint;

        foreach (Rectangle cell in _cells)
        {
            cell.Fill = tint;
        }
    }

    private void ApplySize()
    {
        double size = Size;
        Width = size;
        Height = size;
        _font.FontSize = size;
        _pixels.Width = size;
        _pixels.Height = size;
        Layout();
    }

    /// <summary>Place les pixels sur la grille physique de l'écran.</summary>
    private void Layout()
    {
        if (_mask is null)
        {
            return;
        }

        double scale = XamlRoot?.RasterizationScale ?? 1.0;
        int sidePx = Math.Max(PixelGlyphs.Size, (int)Math.Round(Size * scale));

        // Un pixel d'écran entre deux pixels de l'icône dès que chacun garde au
        // moins deux pixels d'écran ; sinon, pas d'écart.
        int gapPx = (sidePx - ((PixelGlyphs.Size - 1) * 1)) / PixelGlyphs.Size >= 2 ? 1 : 0;
        int cellPx = Math.Max(1, (sidePx - ((PixelGlyphs.Size - 1) * gapPx)) / PixelGlyphs.Size);
        int gridPx = (PixelGlyphs.Size * cellPx) + ((PixelGlyphs.Size - 1) * gapPx);
        double offset = Math.Floor((sidePx - gridPx) / 2.0) / scale;

        for (int i = 0; i < _cells.Length; i++)
        {
            int row = i / PixelGlyphs.Size, column = i % PixelGlyphs.Size;
            Rectangle cell = _cells[i];
            cell.Width = cellPx / scale;
            cell.Height = cellPx / scale;
            cell.RadiusX = cell.RadiusY = cellPx >= 3 ? 0.5 / scale : 0;
            Canvas.SetLeft(cell, offset + (column * (cellPx + gapPx) / scale));
            Canvas.SetTop(cell, offset + (row * (cellPx + gapPx) / scale));
            cell.Opacity = _mask[i] ? 1 : UnlitOpacity;

            if (_visualDriven)
            {
                Visual visual = ElementCompositionPreview.GetElementVisual(cell);
                visual.StopAnimation("Opacity");
                visual.Opacity = (float)cell.Opacity;
            }
        }
    }

    /// <summary>
    /// Glyphes qui migrent (P6) : chaque pixel de la nouvelle icône part de la
    /// place d'un pixel de l'ancienne et y glisse, avec un léger décalage de
    /// balayage. Faux si rien ne peut voyager (animations réduites, icône vide).
    /// </summary>
    private bool Migrate(IReadOnlyList<bool> from, IReadOnlyList<bool> to)
    {
        if (!AnimationsEnabled || _frames is not null || !IsLoaded)
        {
            return false;
        }

        IReadOnlyList<PixelMove> moves = GlyphMorph.Pair(from, to);

        if (moves.Count == 0 || _cells.Length < 2)
        {
            return false;
        }

        double pitch = Canvas.GetLeft(_cells[1]) - Canvas.GetLeft(_cells[0]);

        try
        {
            Compositor compositor = ElementCompositionPreview.GetElementVisual(_cells[0]).Compositor;
            CompositionEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.3f, 1.25f), new System.Numerics.Vector2(0.5f, 1f));

            foreach (PixelMove move in moves)
            {
                Rectangle cell = _cells[move.To];
                ElementCompositionPreview.SetIsTranslationEnabled(cell, true);
                Visual visual = ElementCompositionPreview.GetElementVisual(cell);
                (int dx, int dy) = GlyphMorph.Offset(move);

                Vector3KeyFrameAnimation travel = compositor.CreateVector3KeyFrameAnimation();
                travel.InsertKeyFrame(0f, new System.Numerics.Vector3((float)(dx * pitch), (float)(dy * pitch), 0));
                travel.InsertKeyFrame(1f, System.Numerics.Vector3.Zero, ease);
                travel.Duration = TimeSpan.FromMilliseconds(GlyphMorph.TravelMilliseconds);
                travel.DelayTime = TimeSpan.FromMilliseconds(move.Order * GlyphMorph.StaggerMilliseconds);
                travel.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                visual.StartAnimation("Translation", travel);
            }

            return true;
        }
        catch (Exception)
        {
            // Compositeur indisponible : l'icône est déjà posée à sa place.
            return false;
        }
    }

    /// <summary>Les pixels allumés s'éclairent du centre vers les bords.</summary>
    private void LightUp()
    {
        if (_mask is null || !AnimationsEnabled)
        {
            return;
        }

        try
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (!_mask[i])
                {
                    continue;
                }

                Visual visual = ElementCompositionPreview.GetElementVisual(_cells[i]);
                Compositor compositor = visual.Compositor;
                ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(0f, (float)UnlitOpacity);
                fade.InsertKeyFrame(1f, 1f);
                fade.Duration = TimeSpan.FromMilliseconds(140);
                fade.DelayTime = TimeSpan.FromMilliseconds(30 + (PixelGlyphs.LightOrder(i) * 38));
                fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                visual.StartAnimation("Opacity", fade);
            }
        }
        catch (Exception)
        {
            // Compositeur indisponible : l'icône est déjà dessinée, allumée.
        }
    }
}
