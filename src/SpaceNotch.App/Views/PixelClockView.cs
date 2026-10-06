using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Views;

/// <summary>
/// Horloge à palettes en pixels (M1) : chaque caractère est une petite palette
/// de 3 × 5 pixels, comme un tableau d'aéroport. Quand une minute passe, seule
/// la palette qui change bascule (320 ms) et montre le nouveau chiffre.
/// </summary>
public sealed partial class PixelClockView : StackPanel
{
    /// <summary>Côté d'un pixel de chiffre, en DIPs.</summary>
    private const double Cell = 2;

    private static readonly TimeSpan Half = TimeSpan.FromMilliseconds(160);

    private readonly List<Canvas> _palettes = [];
    private string? _shown;
    private Brush? _tint;

    public PixelClockView()
    {
        Orientation = Orientation.Horizontal;
        Spacing = Cell;
        IsHitTestVisible = false;
    }

    /// <summary>Faux quand Windows réduit les animations : le chiffre change sur place.</summary>
    public bool Animate { get; set; } = true;

    /// <summary>
    /// Teinte des pixels allumés. Propriété de dépendance : le XAML la donne
    /// par <c>{ThemeResource}</c>, qu'une propriété ordinaire ne peut recevoir.
    /// </summary>
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint), typeof(Brush), typeof(PixelClockView), new PropertyMetadata(null, (d, e) => ((PixelClockView)d).OnTintChanged((Brush?)e.NewValue)));

    /// <summary>Teinte des pixels allumés.</summary>
    public Brush? Tint
    {
        get => (Brush?)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    private void OnTintChanged(Brush? value)
    {
        _tint = value;

        if (_shown is not null)
        {
            string text = _shown;
            _shown = null;
            _palettes.Clear();
            Children.Clear();
            Show(text);
        }
    }

    /// <summary>Affiche un texte (« 14:07 ») ; les palettes qui changent basculent.</summary>
    public void Show(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.Equals(text, _shown, StringComparison.Ordinal))
        {
            return;
        }

        bool rebuild = _shown is null || _shown.Length != text.Length;
        IReadOnlyList<int> changed = PixelFont.Changed(_shown, text);
        _shown = text;

        if (rebuild)
        {
            _palettes.Clear();
            Children.Clear();

            foreach (char c in text)
            {
                Canvas palette = NewPalette(c);
                _palettes.Add(palette);
                Children.Add(palette);
            }

            return;
        }

        foreach (int index in changed)
        {
            Flip(_palettes[index], text[index]);
        }
    }

    private Canvas NewPalette(char c)
    {
        var palette = new Canvas
        {
            Width = (c == ':' ? 1 : PixelFont.Width) * Cell,
            Height = PixelFont.Height * Cell
        };

        Paint(palette, c);
        return palette;
    }

    private void Paint(Canvas palette, char c)
    {
        palette.Tag = c;
        palette.Children.Clear();
        IReadOnlyList<bool> mask = PixelFont.Resolve(c);

        for (int i = 0; i < mask.Count; i++)
        {
            if (!mask[i])
            {
                continue;
            }

            int row = i / PixelFont.Width, column = i % PixelFont.Width;

            // Le deux-points n'a qu'une colonne utile : la palette reste étroite.
            if (c == ':')
            {
                column = 0;
            }

            var pixel = new Rectangle { Width = Cell, Height = Cell, Fill = _tint };
            Canvas.SetLeft(pixel, column * Cell);
            Canvas.SetTop(pixel, row * Cell);
            palette.Children.Add(pixel);
        }
    }

    /// <summary>La palette se replie sur son axe horizontal, change de chiffre, se déplie.</summary>
    private void Flip(Canvas palette, char c)
    {
        if (!Animate)
        {
            Paint(palette, c);
            return;
        }

        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(palette);
            visual.CenterPoint = new Vector3((float)(palette.Width / 2), (float)(palette.Height / 2), 0);
            Compositor compositor = visual.Compositor;

            ScalarKeyFrameAnimation fold = compositor.CreateScalarKeyFrameAnimation();
            fold.InsertKeyFrame(1f, 0f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.5f, 0f), new Vector2(1f, 1f)));
            fold.Duration = Half;

            CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Scale.Y", fold);
            batch.End();
            batch.Completed += (_, _) => DispatcherQueue.TryEnqueueSafely(() =>
            {
                Paint(palette, c);
                ScalarKeyFrameAnimation unfold = compositor.CreateScalarKeyFrameAnimation();
                unfold.InsertKeyFrame(0f, 0f);
                unfold.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0.3f, 1.4f)));
                unfold.Duration = Half;
                visual.StartAnimation("Scale.Y", unfold);
            });
        }
        catch (Exception)
        {
            Paint(palette, c);
        }
    }

    // ---- Visage du repos (transition C) : les yeux deviennent les deux-points ----

    /// <summary>Écart vertical des deux points au centre de la palette, en DIPs (rangées 1 et 3 sur 5).</summary>
    public const double ColonHalfGap = 2;

    /// <summary>Côté d'un point des deux-points, en DIPs.</summary>
    public const double ColonDot = Cell;

    private Canvas? Colon => _palettes.Find(p => p.Tag is ':');

    /// <summary>Centre des deux-points dans le repère de <paramref name="relativeTo"/> ; null sans deux-points.</summary>
    public global::Windows.Foundation.Point? ColonCenter(UIElement relativeTo)
    {
        if (Colon is not { } colon)
        {
            return null;
        }

        return colon.TransformToVisual(relativeTo).TransformPoint(new global::Windows.Foundation.Point(Cell / 2, colon.Height / 2));
    }

    /// <summary>Toutes les palettes repliées : l'horloge attend que les yeux deviennent ses deux-points.</summary>
    public void FoldAll()
    {
        foreach (Canvas palette in _palettes)
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(palette);
            visual.StopAnimation("Scale.Y");
            visual.CenterPoint = new Vector3((float)(palette.Width / 2), (float)(palette.Height / 2), 0);
            visual.Scale = new Vector3(1, 0, 1);
        }
    }

    /// <summary>Palettes dépliées sans animation.</summary>
    public void UnfoldAll()
    {
        foreach (Canvas palette in _palettes)
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(palette);
            visual.StopAnimation("Scale.Y");
            visual.Scale = Vector3.One;
        }
    }

    /// <summary>
    /// Les deux-points apparaissent tels quels, puis les chiffres se déplient de
    /// part et d'autre, les plus proches d'abord (160 ms, avec rebond).
    /// </summary>
    public void UnfoldFromColon()
    {
        int colon = _palettes.FindIndex(p => p.Tag is ':');

        if (colon < 0 || !Animate)
        {
            UnfoldAll();
            return;
        }

        for (int i = 0; i < _palettes.Count; i++)
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(_palettes[i]);
            int distance = Math.Abs(i - colon);

            if (distance == 0)
            {
                visual.Scale = Vector3.One;
                continue;
            }

            Compositor compositor = visual.Compositor;
            ScalarKeyFrameAnimation unfold = compositor.CreateScalarKeyFrameAnimation();
            unfold.InsertKeyFrame(0f, 0f);
            unfold.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0.3f, 1.4f)));
            unfold.Duration = Half;
            unfold.DelayTime = TimeSpan.FromMilliseconds((distance - 1) * 60);
            unfold.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation("Scale.Y", unfold);
        }
    }

    /// <summary>Les chiffres se replient vers les deux-points, les plus éloignés d'abord ; rend la durée totale.</summary>
    public TimeSpan FoldToColon()
    {
        int colon = _palettes.FindIndex(p => p.Tag is ':');

        if (colon < 0 || !Animate)
        {
            FoldAll();
            return TimeSpan.Zero;
        }

        int far = 0;

        for (int i = 0; i < _palettes.Count; i++)
        {
            far = Math.Max(far, Math.Abs(i - colon));
        }

        for (int i = 0; i < _palettes.Count; i++)
        {
            int distance = Math.Abs(i - colon);

            if (distance == 0)
            {
                continue;
            }

            Visual visual = ElementCompositionPreview.GetElementVisual(_palettes[i]);
            visual.CenterPoint = new Vector3((float)(_palettes[i].Width / 2), (float)(_palettes[i].Height / 2), 0);
            Compositor compositor = visual.Compositor;
            ScalarKeyFrameAnimation fold = compositor.CreateScalarKeyFrameAnimation();
            fold.InsertKeyFrame(1f, 0f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.5f, 0f), new Vector2(1f, 1f)));
            fold.Duration = TimeSpan.FromMilliseconds(140);
            fold.DelayTime = TimeSpan.FromMilliseconds((far - distance) * 50);
            visual.StartAnimation("Scale.Y", fold);
        }

        return TimeSpan.FromMilliseconds(140 + ((far - 1) * 50));
    }

    /// <summary>Cache les deux-points seuls (les yeux prennent leur place).</summary>
    public void HideColon()
    {
        if (Colon is { } colon)
        {
            ElementCompositionPreview.GetElementVisual(colon).Scale = new Vector3(1, 0, 1);
        }
    }
}
