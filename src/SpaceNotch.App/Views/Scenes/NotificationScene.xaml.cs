using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Presentation;
using SpaceNotch_App.Views;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Groupe de notifications d'une application, ouvert.
/// </summary>
public sealed partial class NotificationScene : UserControl, IIslandSceneView
{
    /// <summary>Messages précédents montrés sous le plus récent.</summary>
    private const int HistoryLines = 3;

    public event Action? DismissRequested;

    public NotificationScene()
    {
        InitializeComponent();
    }

    public FrameworkElement Root => this;

    /// <summary>Le glyphe et le titre prolongent ceux de la forme compacte.</summary>
    public FrameworkElement? AnchorFor(MorphAnchorKind kind) => kind switch
    {
        MorphAnchorKind.Icon or MorphAnchorKind.Artwork => AppIcon,
        MorphAnchorKind.Title => TitleText,
        _ => null
    };

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (activity.Payload is NotificationGroupPayload group && group.Count > 0)
        {
            AppSourceText.Text = group.AppName;
            CountText.Text = group.Count > 1
                ? group.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)
                : string.Empty;
            CountText.Visibility = group.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            TitleText.Text = group.Items[0].Title;
            BodyText.Text = group.Items[0].Body;

            RebuildHistory(group);
            return;
        }

        // Une notification sans groupe — un greffon, par exemple — se lit quand
        // même : titre et sous-titre de l'activité.
        AppSourceText.Text = activity.Source ?? activity.Eyebrow ?? string.Empty;
        CountText.Visibility = Visibility.Collapsed;
        TitleText.Text = activity.Title;
        BodyText.Text = activity.Subtitle ?? string.Empty;
        HistoryHost.Children.Clear();
    }

    private void RebuildHistory(NotificationGroupPayload group)
    {
        HistoryHost.Children.Clear();

        foreach (NotificationItem item in group.Items.Skip(1).Take(HistoryLines))
        {
            HistoryHost.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(item.Body) ? item.Title : $"{item.Title} — {item.Body}",
                // Style et encre repris des éléments déjà résolus par le thème :
                // une recherche de ressource à la main échouerait à l'exécution
                // sur une clé de dictionnaire de thème.
                Style = BodyText.Style,
                MaxLines = 1,
                Foreground = AppSourceText.Foreground
            });
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        DismissRequested?.Invoke();
    }
}
