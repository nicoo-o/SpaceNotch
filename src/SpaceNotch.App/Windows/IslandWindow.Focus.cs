using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch_App.Views;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Pomodoro autour de la silhouette (F11) : pendant une session de focus, un
/// fil fin teinté suit l'intérieur du contour de la notch — un U ouvert vers
/// l'écran — et ses deux bras raccourcissent ensemble vers le milieu du bas à
/// mesure que le temps passe. Rien ne se dessine autour de la notch : le fil
/// est rentré dans la forme. Il n'existe que pour la notch attachée en haut,
/// pendant une session qui avance.
/// </summary>
public sealed partial class IslandWindow
{
    private IslandFootprint? _focusFootprint;
    private double _focusDrawn = -1;

    /// <summary>Redessine le fil ; <paramref name="footprint"/> nul garde la dernière forme posée.</summary>
    private void UpdateFocusTrace(IslandFootprint? footprint = null)
    {
        if (footprint is { } shape)
        {
            if (_focusFootprint is { } previous && previous.Equals(shape) && FocusTrace.Visibility == Visibility.Visible)
            {
                footprint = null;
            }
            else
            {
                _focusFootprint = shape;
                _focusDrawn = -1;
            }
        }

        bool show = _pomodoroFeature.IsSessionRunning
            && _focusFootprint is not null
            && !UsesFloatingGeometry
            && !UsesSideTab;

        if (!show)
        {
            if (FocusTrace.Visibility != Visibility.Collapsed)
            {
                FocusTrace.Visibility = Visibility.Collapsed;
                FocusTrace.Data = null;
                _focusDrawn = -1;
            }

            return;
        }

        IslandFootprint current = _focusFootprint!.Value;
        double remaining = _pomodoroFeature.RemainingFraction;

        // Un demi-millième de tour ne se voit pas : pas de nouveau tracé pour lui.
        if (Math.Abs(remaining - _focusDrawn) < 0.0005)
        {
            return;
        }

        _focusDrawn = remaining;
        NotchGeometry geometry = _settings.Geometry;
        ShapePoint[] ring = OutlineTrim.Ring(
            current.Width,
            current.Height,
            geometry.RadiusFor(current),
            geometry.Smoothing,
            geometry.ShoulderFor(current));

        var trace = OutlineTrim.Centered(OutlineTrim.OpenTop(ring), remaining);

        if (trace.Count < 2)
        {
            FocusTrace.Visibility = Visibility.Collapsed;
            return;
        }

        var segment = new PolyLineSegment();

        for (int i = 1; i < trace.Count; i++)
        {
            segment.Points.Add(new global::Windows.Foundation.Point(trace[i].X, trace[i].Y));
        }

        var figure = new PathFigure
        {
            StartPoint = new global::Windows.Foundation.Point(trace[0].X, trace[0].Y),
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(segment);

        var path = new PathGeometry();
        path.Figures.Add(figure);

        FocusTrace.Data = path;
        FocusTrace.Stroke = new SolidColorBrush(StatePalette.Tint(IslandActivityState.TimerActive));
        FocusTrace.Visibility = Visibility.Visible;
    }
}
