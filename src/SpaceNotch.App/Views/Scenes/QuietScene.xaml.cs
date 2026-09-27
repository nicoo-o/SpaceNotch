using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Ne pas déranger (F9) : pendant le calme, le compte de ce qui attend ; à la
/// sortie, un résumé par application — les plus bavardes d'abord, trois au
/// plus, la dernière notification de chacune en aperçu.
/// </summary>
public sealed partial class QuietScene : UserControl, IIslandSceneView
{
    private const int MaxGroups = 3;

    private IReadOnlyList<QuietGroup>? _shown;

    public QuietScene()
    {
        InitializeComponent();
    }

    /// <summary>Un résumé, sans action.</summary>
    public event EventHandler<IslandActionRequest>? ActionRequested
    {
        add { }
        remove { }
    }

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        TitleText.Text = activity.Title ?? string.Empty;
        SubtitleText.Text = activity.Subtitle ?? string.Empty;

        if (activity.Payload is not QuietPayload payload || ReferenceEquals(payload.Groups, _shown))
        {
            return;
        }

        _shown = payload.Groups;
        Groups.Children.Clear();

        for (int i = 0; i < payload.Groups.Count && i < MaxGroups; i++)
        {
            Groups.Children.Add(GroupRow(payload.Groups[i]));
        }
    }

    private static Grid GroupRow(QuietGroup group)
    {
        var row = new Grid { ColumnSpacing = 8, Height = 20 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var app = new TextBlock
        {
            Text = group.App,
            FontSize = 11.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("NfTextPrimaryBrush")
        };

        var latest = new TextBlock
        {
            Text = group.Latest,
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("NfTextSecondaryBrush")
        };

        var count = new TextBlock
        {
            Text = group.Count.ToString(CultureInfo.CurrentCulture),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("NfTextTertiaryBrush")
        };

        Grid.SetColumn(latest, 1);
        Grid.SetColumn(count, 2);
        row.Children.Add(app);
        row.Children.Add(latest);
        row.Children.Add(count);

        return row;
    }

    private static Brush? Brush(string key)
        => Application.Current.Resources.TryGetValue(key, out object? value) ? value as Brush : null;
}
