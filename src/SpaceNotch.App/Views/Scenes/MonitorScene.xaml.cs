using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.SystemInfo;
using SpaceNotch.Features.Power;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Moniteur système (F6) : le processus qui sature le processeur, sa part, la
/// courbe des trente dernières mesures avec le seuil d'alerte, et un bouton
/// pour le fermer — jamais proposé pour un processus du système.
/// </summary>
public sealed partial class MonitorScene : UserControl, IIslandSceneView
{
    private static readonly global::Windows.UI.Color Red = global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B);

    private string? _activityId;
    private IReadOnlyList<double> _history = [];

    public MonitorScene()
    {
        InitializeComponent();
        var red = new SolidColorBrush(Red);
        CpuGlyph.Tint = red;
        CurveLine.Stroke = red;
        CurveFill.Fill = red;
        ThresholdLine.Fill = red;
        CurveHost.SizeChanged += (_, _) => DrawCurve();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activityId = activity.Id;

        if (activity.Payload is not MonitorPayload payload)
        {
            return;
        }

        ProcessText.Text = payload.Process;
        LoadText.Text = activity.Subtitle ?? string.Empty;

        bool closable = payload.ProcessId > 0;
        CloseProcess.Visibility = closable ? Visibility.Visible : Visibility.Collapsed;
        CloseProcess.Content = SpaceNotch.Core.Localization.Lang.T("Fermer", "Close");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            CloseProcess,
            SpaceNotch.Core.Localization.Lang.T($"Fermer {payload.Process}", $"Close {payload.Process}"));

        _history = payload.History;
        DrawCurve();
    }

    private void DrawCurve()
    {
        double width = CurveHost.ActualWidth, height = CurveHost.ActualHeight;

        if (width <= 0 || height <= 0 || _history.Count < 2)
        {
            CurveLine.Data = null;
            CurveFill.Data = null;
            return;
        }

        double step = width / (CpuWatch.HistoryLength - 1);
        double start = width - ((_history.Count - 1) * step);
        var line = new PathFigure { IsClosed = false, IsFilled = false };
        var fill = new PathFigure { IsClosed = true, IsFilled = true, StartPoint = new global::Windows.Foundation.Point(start, height) };
        var lineSegment = new PolyLineSegment();
        var fillSegment = new PolyLineSegment();

        for (int i = 0; i < _history.Count; i++)
        {
            var point = new global::Windows.Foundation.Point(start + (i * step), height * (1 - (_history[i] / 100)));

            if (i == 0)
            {
                line.StartPoint = point;
            }
            else
            {
                lineSegment.Points.Add(point);
            }

            fillSegment.Points.Add(point);
        }

        fillSegment.Points.Add(new global::Windows.Foundation.Point(width, height));
        line.Segments.Add(lineSegment);
        fill.Segments.Add(fillSegment);

        var lineGeometry = new PathGeometry();
        lineGeometry.Figures.Add(line);
        var fillGeometry = new PathGeometry();
        fillGeometry.Figures.Add(fill);

        CurveLine.Data = lineGeometry;
        CurveFill.Data = fillGeometry;
        ThresholdLine.Margin = new Thickness(0, height * (1 - (CpuWatch.Threshold / 100)), 0, 0);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        if (_activityId is not null)
        {
            ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, SystemMonitorFeature.CloseAction));
        }
    }
}
