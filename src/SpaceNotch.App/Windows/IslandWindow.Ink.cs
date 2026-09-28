using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Clic d'encre (A5) : chaque appui dans la notch fait partir du point de
/// contact un anneau de pixels qui s'élargit et s'éteint en 180 ms. L'anneau
/// est découpé par la silhouette : il ne déborde jamais de la notch (E1).
/// </summary>
public sealed partial class IslandWindow
{
    private static readonly SolidColorBrush InkBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 0x7F, 0xE6, 0xFF));

    private Canvas? _inkLayer;
    private DispatcherQueueTimer? _inkTimer;
    private int _inkFrame;
    private global::Windows.Foundation.Point _inkAt;
    private readonly List<Rectangle> _inkPixels = [];

    /// <summary>Branche l'anneau sur tous les appuis, y compris ceux qu'un bouton a déjà traités.</summary>
    private void HookInk()
    {
        IslandBody.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnInkPressed), handledEventsToo: true);

        // Projecteur tramé (A4) : dans une scène ouverte, la trame suit le curseur.
        IslandBody.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnSpotMoved), handledEventsToo: true);
    }

    private void OnSpotMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!UseSpringAnimations())
        {
            return;
        }

        global::Windows.Foundation.Point at = e.GetCurrentPoint(IslandBody).Position;
        SceneTrame.Spotlight((at.X, at.Y));
    }

    private void OnInkPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!UseSpringAnimations())
        {
            return;
        }

        PlayInk(e.GetCurrentPoint(IslandBody).Position);
    }

    private void PlayInk(global::Windows.Foundation.Point at)
    {
        if (_inkLayer is null)
        {
            _inkLayer = new Canvas { IsHitTestVisible = false };
            IslandBody.Children.Add(_inkLayer);
        }

        _inkLayer.Clip = new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, IslandBody.ActualWidth, IslandBody.ActualHeight) };
        _inkAt = at;
        _inkFrame = 0;

        if (_inkTimer is null)
        {
            _inkTimer = DispatcherQueue.CreateTimer();
            _inkTimer.Interval = TimeSpan.FromMilliseconds(InkRing.DurationMilliseconds / InkRing.Frames);
            _inkTimer.IsRepeating = true;
            _inkTimer.Tick += (_, _) => InkTick();
        }

        InkTick();
        _inkTimer.Start();
    }

    private void InkTick()
    {
        Canvas layer = _inkLayer!;

        if (_inkFrame >= InkRing.Frames)
        {
            _inkTimer?.Stop();
            layer.Children.Clear();
            _inkPixels.Clear();
            return;
        }

        IReadOnlyList<(int X, int Y)> ring = InkRing.Circle(InkRing.RadiusAt(_inkFrame));
        double opacity = InkRing.OpacityAt(_inkFrame);

        // Les rectangles déjà créés sont réutilisés d'une image à l'autre.
        while (_inkPixels.Count < ring.Count)
        {
            var pixel = new Rectangle { Width = 1.6, Height = 1.6, Fill = InkBrush };
            _inkPixels.Add(pixel);
            layer.Children.Add(pixel);
        }

        for (int i = 0; i < _inkPixels.Count; i++)
        {
            Rectangle pixel = _inkPixels[i];

            if (i >= ring.Count)
            {
                pixel.Visibility = Visibility.Collapsed;
                continue;
            }

            pixel.Visibility = Visibility.Visible;
            pixel.Opacity = opacity;
            Canvas.SetLeft(pixel, _inkAt.X + (ring[i].X * InkRing.Pitch) - 0.8);
            Canvas.SetTop(pixel, _inkAt.Y + (ring[i].Y * InkRing.Pitch) - 0.8);
        }

        _inkFrame++;
    }
}
