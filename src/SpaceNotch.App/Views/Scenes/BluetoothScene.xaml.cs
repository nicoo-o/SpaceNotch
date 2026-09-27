using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;
using Windows.Foundation;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Un appareil Bluetooth qui se connecte (point cyan, anneau de batterie), se
/// déconnecte (atténué) ou dont la batterie faiblit (ambre).
/// </summary>
public sealed partial class BluetoothScene : UserControl, IIslandSceneView
{
    private static readonly global::Windows.UI.Color Amber = global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xB8, 0x6B);
    private static readonly global::Windows.UI.Color Green = global::Windows.UI.Color.FromArgb(0xFF, 0x8F, 0xF0, 0xA4);

    public BluetoothScene()
    {
        InitializeComponent();
    }

    /// <summary>Un indicateur, sans action.</summary>
    public event EventHandler<IslandActionRequest>? ActionRequested
    {
        add { }
        remove { }
    }

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (activity.Payload is not BluetoothPayload payload)
        {
            NameText.Text = activity.Title;
            StateText.Text = activity.Subtitle ?? string.Empty;
            BatteryHost.Visibility = Visibility.Collapsed;
            return;
        }

        NameText.Text = payload.Name;
        KindIcon.Key = payload.IconKey;

        double dim = payload.IsConnected ? 1 : 0.5;
        IconPlate.Opacity = dim;
        NameText.Opacity = payload.IsConnected ? 1 : 0.7;
        StateDot.Visibility = payload.IsConnected ? Visibility.Visible : Visibility.Collapsed;

        bool low = payload.IsConnected && payload.IsBatteryLow;
        StateText.Text = !payload.IsConnected
            ? Lang.T("Déconnecté", "Disconnected")
            : low ? Lang.T($"Batterie faible · {payload.BatteryPercent} %", $"Low battery · {payload.BatteryPercent}%") : Lang.T("Connecté", "Connected");
        StateText.Foreground = low
            ? new SolidColorBrush(Amber)
            : (Brush)Application.Current.Resources["NfTextSecondaryBrush"];

        if (payload.IsConnected && payload.BatteryPercent is int level)
        {
            BatteryHost.Visibility = Visibility.Visible;
            BatteryText.Text = level.ToString(System.Globalization.CultureInfo.CurrentCulture);
            BatteryArc.Stroke = new SolidColorBrush(low ? Amber : Green);
            BatteryArc.Data = Arc(level / 100.0, center: 17, radius: 15.5);
        }
        else
        {
            BatteryHost.Visibility = Visibility.Collapsed;
        }

    }

    /// <summary>Arc de cercle depuis midi, dans le sens horaire, sur une fraction du tour.</summary>
    private static PathGeometry Arc(double fraction, double center, double radius)
    {
        fraction = Math.Clamp(fraction, 0.001, 0.999);
        double angle = fraction * 2 * Math.PI;

        var start = new Point(center, center - radius);
        var end = new Point(center + (radius * Math.Sin(angle)), center - (radius * Math.Cos(angle)));

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = fraction > 0.5
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
