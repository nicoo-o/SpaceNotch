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

        // Résumé (I1) : ce qui te concerne d'abord, puis le reste sur une ligne.
        if (payload.Digest is { } digest)
        {
            foreach (SpaceNotch.Core.Assistant.DigestLine line in digest.Important)
            {
                Groups.Children.Add(Row(line.App, line.Line, null, primary: true));
            }

            if (digest.Rest is { } rest && Groups.Children.Count < MaxGroups + 1)
            {
                Groups.Children.Add(Row(string.Empty, rest, null, primary: false));
            }

            return;
        }

        for (int i = 0; i < payload.Groups.Count && i < MaxGroups; i++)
        {
            QuietGroup group = payload.Groups[i];
            Groups.Children.Add(Row(group.App, group.Latest, group.Count.ToString(CultureInfo.CurrentCulture), primary: false));
        }
    }

    /// <summary>Fond des pastilles d'application, comme les puces de la maquette.</summary>
    private static readonly SolidColorBrush ChipBrush = new(global::Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    private static Grid Row(string appName, string text, string? countText, bool primary)
    {
        var row = new Grid { ColumnSpacing = 10, MinHeight = 24 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // L'application en pastille grise (I1) : « Teams », « Outlook ».
        var app = new Border
        {
            Background = ChipBrush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 3),
            MaxWidth = 110,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = appName.Length == 0 ? Visibility.Collapsed : Visibility.Visible,
            Child = new TextBlock
            {
                Text = appName,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush("NfTextPrimaryBrush")
            }
        };

        var latest = new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(primary ? "NfTextSecondaryBrush" : "NfTextTertiaryBrush")
        };

        var count = new TextBlock
        {
            Text = countText ?? string.Empty,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("NfTextTertiaryBrush")
        };

        // Sans application (la ligne « Et 9 autres »), le texte prend toute la largeur.
        Grid.SetColumn(latest, appName.Length == 0 ? 0 : 1);
        Grid.SetColumnSpan(latest, appName.Length == 0 ? 2 : 1);
        Grid.SetColumn(count, 2);
        row.Children.Add(app);
        row.Children.Add(latest);
        row.Children.Add(count);

        return row;
    }

    private static Brush? Brush(string key)
        => Application.Current.Resources.TryGetValue(key, out object? value) ? value as Brush : null;
}
