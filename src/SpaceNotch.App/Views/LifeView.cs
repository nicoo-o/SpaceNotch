using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Motion;
using Windows.UI;

namespace SpaceNotch_App.Views;

/// <summary>
/// Écran de veille (P5) : le jeu de la vie en pixels, dans la notch élargie
/// au repos. Une cellule qui vient de naître est cyan, lilas en grandissant,
/// puis indigo sombre. Les règles et la graine viennent de <see cref="LifeGrid"/>.
/// </summary>
public sealed partial class LifeView : Canvas
{
    /// <summary>Colonnes et rangées : la maquette, 316 × 58 en pixels de 4.</summary>
    public const int Columns = 79;

    public const int Rows = 14;

    private const double Pixel = 3.0;
    private const double Pitch = 4.0;

    private static readonly SolidColorBrush Young = new(Color.FromArgb(0xFF, 0x7F, 0xE6, 0xFF));
    private static readonly SolidColorBrush Grown = new(Color.FromArgb(0xFF, 0xB9, 0xA8, 0xFF));
    private static readonly SolidColorBrush Old = new(Color.FromArgb(0xFF, 0x3B, 0x3F, 0x7A));

    private readonly Rectangle[] _cells = new Rectangle[Columns * Rows];
    private LifeGrid? _grid;

    public LifeView()
    {
        IsHitTestVisible = false;
        Width = (Columns * Pitch) - (Pitch - Pixel);
        Height = (Rows * Pitch) - (Pitch - Pixel);

        for (int i = 0; i < _cells.Length; i++)
        {
            var cell = new Rectangle { Width = Pixel, Height = Pixel, Opacity = 0 };
            SetLeft(cell, (i % Columns) * Pitch);
            SetTop(cell, (i / Columns) * Pitch);
            Children.Add(cell);
            _cells[i] = cell;
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

                int age = _grid.Age(column, row);
                cell.Fill = age < 2 ? Young : age < 6 ? Grown : Old;
                cell.Opacity = 1;
            }
        }
    }
}
