using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using Rect = Windows.Foundation.Rect;

namespace SpaceNotch_App.Views;

/// <summary>
/// Fondu en pixels (A2) : quand une activité en remplace une autre, le
/// nouveau contenu apparaît sous une grille de pixels noirs qui s'éteignent
/// en vague, du centre vers les bords. La matière du logo, appliquée aux
/// transitions. Seules les cases entièrement dans la silhouette sont
/// dessinées : jamais un carré noir ne dépasse de la notch.
/// </summary>
public sealed partial class PixelDissolveView : Microsoft.UI.Xaml.Controls.Grid
{
    private readonly Microsoft.UI.Xaml.Shapes.Path _path = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };

    /// <summary>Durée de la vague qui découvre le contenu, en secondes.</summary>
    private const double UncoverSeconds = 0.24;

    private readonly List<(int Column, int Row, Rect Cell)> _cells = [];
    private DispatcherQueueTimer? _timer;
    private DateTime _start;
    private int _columns, _rows;

    public PixelDissolveView()
    {
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_path);
    }

    /// <summary>Joue la vague sur une forme de <paramref name="width"/> × <paramref name="height"/> DIPs.</summary>
    public void Play(IReadOnlyList<ShapePoint> outline, double width, double height, Brush fill)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        _path.Fill = fill;
        _cells.Clear();
        double cell = PixelDissolve.CellDip;
        _columns = (int)Math.Ceiling(width / cell);
        _rows = (int)Math.Ceiling(height / cell);

        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _columns; c++)
            {
                double x = c * cell, y = r * cell;
                double w = Math.Min(cell, width - x), h = Math.Min(cell, height - y);

                if (ShapeHit.Contains(outline, x + 0.5, y + 0.5)
                    && ShapeHit.Contains(outline, x + w - 0.5, y + 0.5)
                    && ShapeHit.Contains(outline, x + 0.5, y + h - 0.5)
                    && ShapeHit.Contains(outline, x + w - 0.5, y + h - 0.5))
                {
                    _cells.Add((c, r, new Rect(x, y, w, h)));
                }
            }
        }

        _start = DateTime.UtcNow;
        _timer ??= CreateTimer();
        Draw(PixelDissolve.SwapAt);
        _timer.Start();
    }

    private DispatcherQueueTimer CreateTimer()
    {
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) =>
        {
            double t = (DateTime.UtcNow - _start).TotalSeconds / UncoverSeconds;

            if (t >= 1)
            {
                timer.Stop();
                _path.Data = null;
                return;
            }

            Draw(PixelDissolve.SwapAt + (t * (1 - PixelDissolve.SwapAt)));
        };

        return timer;
    }

    private void Draw(double progress)
    {
        var group = new GeometryGroup();

        foreach ((int column, int row, Rect cell) in _cells)
        {
            // Une case est noire ou non : des pixels qui s'éteignent, pas un fondu.
            if (PixelDissolve.Cover(column, row, _columns, _rows, progress) >= 0.5)
            {
                group.Children.Add(new RectangleGeometry { Rect = cell });
            }
        }

        _path.Data = group.Children.Count > 0 ? group : null;
    }
}
