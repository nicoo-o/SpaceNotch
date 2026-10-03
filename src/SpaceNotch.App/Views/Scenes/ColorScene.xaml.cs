using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Features.Clipboard;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Couleur copiée (F5) : la nuance à gauche, et HEX, RGB, HSL à droite. Un
/// clic sur un format le recopie ; la ligne dit « Copié » un instant.
/// </summary>
public sealed partial class ColorScene : UserControl, IIslandSceneView
{
    private string? _activityId;
    private ColorCode? _shown;

    public ColorScene()
    {
        InitializeComponent();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activityId = activity.Id;

        if (activity.Payload is not ColorPayload { Color: var color } || color == _shown)
        {
            return;
        }

        _shown = color;
        Swatch.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, color.R, color.G, color.B));

        Formats.Children.Clear();

        foreach ((string label, string value) in new[] { ("HEX", color.Hex), ("RGB", color.Rgb), ("HSL", color.Hsl) })
        {
            Formats.Children.Add(FormatRow(label, value));
        }
    }

    private Button FormatRow(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock { Text = label, FontSize = 10.5, CharacterSpacing = 80, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { Text = value, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = false };
        var copied = new TextBlock { Text = SpaceNotch.Core.Localization.Lang.T("Copié", "Copied"), FontSize = 11.5, Opacity = 0, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(copied, 2);
        grid.Children.Add(name);
        grid.Children.Add(text);
        grid.Children.Add(copied);

        var row = new Button
        {
            Content = grid,
            Height = 24,
            MinHeight = 0,
            Padding = new Thickness(8, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6)
        };

        if (Application.Current.Resources.TryGetValue("NfGhostButtonStyle", out object? style) && style is Style ghost)
        {
            row.Style = ghost;
        }

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, $"{label} {value}");
        row.Click += (_, _) =>
        {
            if (_activityId is null)
            {
                return;
            }

            ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, ClipboardFeature.CopyTextAction, value));
            copied.Opacity = 0.7;
            Microsoft.UI.Dispatching.DispatcherQueueTimer hide = DispatcherQueue.CreateTimer();
            hide.Interval = TimeSpan.FromSeconds(1.2);
            hide.IsRepeating = false;
            hide.Tick += (_, _) => copied.Opacity = 0;
            hide.Start();
        };

        return row;
    }
}
