using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Views;

/// <summary>
/// Sablier de pixels (P4) : au fond de la carte d'un téléchargement, chaque
/// pourcent fait tomber un pixel qui s'empile. La vue ne décide que du dessin ;
/// l'ordre et la place des pixels viennent de <see cref="PixelHeap"/>.
/// </summary>
public sealed partial class PixelHeapView : Canvas
{
    private readonly List<Rectangle> _pixels = [];
    private PixelHeap? _heap;
    private string? _owner;
    private Brush? _tint;

    public PixelHeapView()
    {
        IsHitTestVisible = false;
        Height = (PixelHeap.Pitch * 3) - 1;
        SizeChanged += (_, _) =>
        {
            if (_heap is not null && PixelHeap.ColumnsFor(ActualWidth) != _heap.Columns)
            {
                Clear();
            }
        };
    }

    /// <summary>Faux quand Windows réduit les animations : les pixels se posent sans tomber.</summary>
    public bool Animate { get; set; } = true;

    /// <summary>
    /// Remplit le tas jusqu'à <paramref name="percent"/> (0 à 100) pour
    /// l'activité <paramref name="owner"/>. Une autre activité repart d'un tas vide.
    /// </summary>
    public void Fill(string owner, double percent, Brush tint)
    {
        if (!string.Equals(owner, _owner, StringComparison.Ordinal) || _heap is null)
        {
            Clear();
            _owner = owner;
            _heap = new PixelHeap(PixelHeap.ColumnsFor(Math.Max(ActualWidth, 120)));
        }

        _tint = tint;
        Visibility = Microsoft.UI.Xaml.Visibility.Visible;

        foreach ((int column, int row) in _heap.FillTo(percent))
        {
            var pixel = new Rectangle { Width = PixelHeap.PixelSize, Height = PixelHeap.PixelSize, Fill = _tint, RadiusX = 0.5, RadiusY = 0.5 };
            SetLeft(pixel, column * PixelHeap.Pitch);
            SetTop(pixel, Height - PixelHeap.PixelSize - (row * PixelHeap.Pitch));
            Children.Add(pixel);
            _pixels.Add(pixel);

            if (Animate)
            {
                Fall(pixel, (_pixels.Count % 5) * 30);
            }
        }
    }

    /// <summary>Le tas s'efface : une autre activité, ou la fin du téléchargement.</summary>
    public void Clear()
    {
        Children.Clear();
        _pixels.Clear();
        _heap = null;
        _owner = null;
        Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private static void Fall(Rectangle pixel, int delayMilliseconds)
    {
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(pixel, true);
            Visual visual = ElementCompositionPreview.GetElementVisual(pixel);
            Compositor compositor = visual.Compositor;
            Vector3KeyFrameAnimation fall = compositor.CreateVector3KeyFrameAnimation();
            fall.InsertKeyFrame(0f, new Vector3(0, -34, 0));
            fall.InsertKeyFrame(1f, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new Vector2(0.5f, 0f), new Vector2(1f, 1f)));
            fall.Duration = TimeSpan.FromMilliseconds(PixelHeap.FallMilliseconds);
            fall.DelayTime = TimeSpan.FromMilliseconds(delayMilliseconds);
            fall.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation("Translation", fall);
        }
        catch (Exception)
        {
            // Sans compositeur, le pixel est déjà posé.
        }
    }
}
