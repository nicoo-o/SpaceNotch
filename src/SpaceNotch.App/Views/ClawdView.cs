using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Views;

/// <summary>
/// Clawd, la mascotte de Claude Code, à la place de la grille qui tourne (I4).
/// La vue ne fait que dessiner : les poses, les gestes et les humeurs viennent
/// de <see cref="Clawd"/>. Elle n'anime que tant qu'elle est affichée ; quand
/// Windows réduit les animations, Clawd reste posé dans son humeur.
/// </summary>
public sealed partial class ClawdView : Canvas
{
    private static readonly Dictionary<ClawdInk, SolidColorBrush> Inks = new()
    {
        [ClawdInk.Body] = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xD7, 0x77, 0x57)),
        [ClawdInk.Ask] = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xB1, 0xB9, 0xF9)),
        [ClawdInk.Ok] = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x4E, 0xBA, 0x65)),
        [ClawdInk.Error] = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xFF, 0x6B, 0x80)),
        [ClawdInk.Thought] = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xEE, 0xF0, 0xF6)),
        [ClawdInk.Dust] = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x8A, 0x8F, 0x9E))
    };

    private static readonly SolidColorBrush Filigree = new(Colors.White);

    private readonly Rectangle[] _cells;
    private readonly int _x0;
    private readonly int _y0;
    private readonly int _columns;
    private readonly int _rows;
    private DispatcherQueueTimer? _timer;
    private DateTime _start = DateTime.UtcNow;
    private double _pitch = 1.2;
    private ClawdStyle _style = ClawdStyle.Faithful;
    private ClawdMood _mood = ClawdMood.Thinking;
    private bool _animate = true;

    public ClawdView()
    {
        IsHitTestVisible = false;
        (_x0, _y0, _columns, _rows) = Clawd.Crop;
        _cells = new Rectangle[_columns * _rows];

        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new Rectangle { Opacity = 0 };
            Children.Add(_cells[i]);
        }

        Layout();
        Loaded += (_, _) => Sync();
        Unloaded += (_, _) => _timer?.Stop();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Sync());
    }

    /// <summary>Taille d'un pixel de Clawd, en DIP.</summary>
    public double Pitch
    {
        get => _pitch;
        set
        {
            double pitch = Math.Max(0.5, value);

            if (Math.Abs(pitch - _pitch) > 0.001)
            {
                _pitch = pitch;
                Layout();
            }
        }
    }

    public ClawdStyle PixelStyle
    {
        get => _style;
        set
        {
            if (_style != value)
            {
                _style = value;
                Layout();
            }
        }
    }

    public ClawdMood Mood
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

    /// <summary>Faux quand Windows réduit les animations : Clawd reste posé.</summary>
    public bool Animate
    {
        get => _animate;
        set
        {
            _animate = value;
            Sync();
        }
    }

    private void Layout()
    {
        Width = _columns * _pitch;
        Height = _rows * _pitch;

        // A : carrés pleins, qui se touchent (un soupçon de recouvrement efface les
        // coutures de l'anticrénelage). C : coins arrondis, joint presque invisible.
        // B : pixels ronds.
        (double size, double radius) = _style switch
        {
            ClawdStyle.Soft => (_pitch * 0.97, _pitch * 0.16),
            ClawdStyle.Notch => (_pitch * 0.76, _pitch * 0.38),
            _ => (_pitch + 0.3, 0.0)
        };

        double offset = (_pitch - size) / 2;

        for (int i = 0; i < _cells.Length; i++)
        {
            Rectangle cell = _cells[i];
            cell.Width = size;
            cell.Height = size;
            cell.RadiusX = radius;
            cell.RadiusY = radius;
            SetLeft(cell, ((i % _columns) * _pitch) + offset);
            SetTop(cell, ((i / _columns) * _pitch) + offset);
        }

        Draw();
    }

    private void Sync()
    {
        bool running = Visibility == Visibility.Visible && IsLoaded && _animate;

        if (!running)
        {
            _timer?.Stop();
            Draw();
            return;
        }

        if (_timer is null)
        {
            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / Clawd.FramesPerSecond);
            _timer.IsRepeating = true;
            _timer.Tick += SpaceNotch_App.Diagnostics.Guard.Tick((_, _) => Draw());
        }

        _timer.Start();
    }

    private void Draw()
    {
        // Posé : une image choisie pour être lisible (yeux ouverts, l'effet visible).
        double seconds = _animate ? (DateTime.UtcNow - _start).TotalSeconds : 0.6;
        IReadOnlyList<ClawdPixel> frame = Clawd.Frame(_mood, seconds);

        foreach (Rectangle cell in _cells)
        {
            if (_style == ClawdStyle.Notch)
            {
                cell.Fill = Filigree;
                cell.Opacity = 0.07;
            }
            else
            {
                cell.Opacity = 0;
            }
        }

        foreach (ClawdPixel pixel in frame)
        {
            int column = pixel.X - _x0;
            int row = pixel.Y - _y0;

            if (column < 0 || column >= _columns || row < 0 || row >= _rows)
            {
                continue;
            }

            Rectangle cell = _cells[(row * _columns) + column];
            cell.Fill = Inks[pixel.Ink];
            cell.Opacity = pixel.Alpha;
        }
    }
}
