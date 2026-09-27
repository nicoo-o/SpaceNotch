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
            batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
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
}
