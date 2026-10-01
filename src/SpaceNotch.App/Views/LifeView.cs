using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Views;

/// <summary>
/// Écran de veille (P5) : le jeu de la vie en pixels, dans la lèvre de la
/// notch au repos. Une cellule qui vient de naître est vive ; elle s'assombrit
/// en vieillissant. Les règles et la graine viennent de <see cref="LifeGrid"/>.
/// </summary>
public sealed partial class LifeView : Canvas
{
    /// <summary>Colonnes et rangées : de quoi tenir dans la lèvre sans l'élargir.</summary>
    public const int Columns = 22;

    public const int Rows = 5;

    private const double Pixel = 2.2;
    private const double Pitch = 3.0;

    private readonly Rectangle[] _cells = new Rectangle[Columns * Rows];
    private LifeGrid? _grid;

    public LifeView()
    {
        IsHitTestVisible = false;
        Width = (Columns * Pitch) - (Pitch - Pixel);
        Height = (Rows * Pitch) - (Pitch - Pixel);

        for (int i = 0; i < _cells.Length; i++)
        {
            var cell = new Rectangle { Width = Pixel, Height = Pixel, RadiusX = 0.4, RadiusY = 0.4, Opacity = 0 };
            SetLeft(cell, (i % Columns) * Pitch);
            SetTop(cell, (i / Columns) * Pitch);
            Children.Add(cell);
            _cells[i] = cell;
        }
    }

    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint), typeof(Brush), typeof(LifeView), new PropertyMetadata(null, (d, e) => ((LifeView)d).OnTintChanged((Brush?)e.NewValue)));

    /// <summary>Couleur des cellules.</summary>
    public Brush? Tint
    {
        get => (Brush?)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    private void OnTintChanged(Brush? value)
    {
        foreach (Rectangle cell in _cells)
        {
            cell.Fill = value;
        }
    }

    /// <summary>Nouvelle nuit : nouvelle graine.</summary>
    public void Seed(uint seed)
    {
        _grid = new LifeGrid(Columns, Rows, seed);
        Draw();
    }

    /// <summary>Une génération de plus.</summary>
    public void Step()
    {
        if (_grid is null)
        {
            Seed((uint)Environment.TickCount);
            return;
        }

        _grid.Step();
        Draw();
    }

    private void Draw()
    {
        if (_grid is null)
        {
            return;
        }

        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                Rectangle cell = _cells[(row * Columns) + column];

                if (!_grid.IsAlive(column, row))
                {
                    cell.Opacity = 0;
                    continue;
                }

                // Vive à la naissance, jamais sous un tiers : une cellule ancienne reste lisible.
                double age = Math.Min(_grid.Age(column, row), LifeGrid.OldAge) / (double)LifeGrid.OldAge;
                cell.Opacity = 1.0 - (0.65 * age);
            }
        }
    }
}
