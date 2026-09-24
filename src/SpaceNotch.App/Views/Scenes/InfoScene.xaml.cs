using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch_App.Views;
using Windows.UI;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Scène générique : icône, titre, sous-titre, et les contrôles que la
/// fonctionnalité déclare.
///
/// C'est la scène qui accueille le contenu d'un greffon tiers. Elle ne connaît
/// donc rien de ce qu'elle affiche : elle projette une activité et construit sa
/// rangée de boutons à partir des actions annoncées. Un greffon peut ainsi
/// proposer un contrôle sans qu'aucune ligne du rendu ne mentionne son domaine.
/// </summary>
public sealed partial class InfoScene : UserControl, IIslandSceneView
{
    /// <summary>Identifiant de l'activité présentée, transmis avec chaque action.</summary>
    private string? _activityId;

    public InfoScene()
    {
        InitializeComponent();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        _activityId = activity.Id;

        TitleText.Text = activity.Title;
        SubtitleText.Text = activity.Subtitle ?? string.Empty;
        SceneIcon.Glyph = GlyphCatalog.Resolve(activity.IconKey);

        RebuildActions(activity.Actions);
    }

    /// <summary>
    /// Construit la rangée de contrôles à partir des actions déclarées.
    ///
    /// L'action principale est mise en avant — fond clair, glyphe sombre — parce
    /// qu'une carte qui propose deux contrôles doit indiquer lequel est le
    /// principal sans que la fonctionnalité ait à le décrire autrement.
    /// </summary>
    private void RebuildActions(IReadOnlyList<ActivityAction> actions)
    {
        ActionHost.Children.Clear();

        if (actions.Count == 0)
        {
            ActionHost.Visibility = Visibility.Collapsed;
            return;
        }

        double height = Token("NfActionHeight", 30.0);

        foreach (ActivityAction action in actions)
        {
            // Un contrôle se distingue par son **trait** et par son encre, jamais
            // par un fond plus clair : c'est la règle « sans hiérarchie » des
            // jetons de conception. La pastille blanche qui précédait était de
            // toute façon condamnée — sur un système en thème sombre, elle aurait
            // été le seul objet clair d'une carte noire.
            var button = new Button
            {
                Padding = Token("NfActionPadding", new Thickness(12, 0, 12, 0)),
                Height = height,
                CornerRadius = new CornerRadius(height / 2),
                BorderThickness = new Thickness(1),
                BorderBrush = Ink(action.IsPrimary ? "NfStrokeStrongBrush" : "NfStrokeSubtleBrush", 0x24),
                Background = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00)),
                Foreground = Ink(action.IsPrimary ? "NfTextPrimaryBrush" : "NfTextSecondaryBrush", 0xC0),
                IsEnabled = action.IsEnabled,
                Content = BuildActionContent(action)
            };

            // L'identification passe par l'étiquette : la vue n'a aucune raison de
            // connaître la signification de l'identifiant qu'elle transporte.
            button.Tag = action.Id;
            button.Click += OnActionClicked;

            ActionHost.Children.Add(button);
        }

        ActionHost.Visibility = Visibility.Visible;
    }

    private static StackPanel BuildActionContent(ActivityAction action)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (!string.IsNullOrEmpty(action.IconKey))
        {
            panel.Children.Add(new FontIcon
            {
                Glyph = GlyphCatalog.Resolve(action.IconKey),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = action.Label,
            FontSize = Token("NfActionFontSize", 11.5),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        return panel;
    }

    /// <summary>
    /// Lit un pinceau des jetons de conception.
    ///
    /// Une clé absente ne doit pas faire disparaître un contrôle : le repli est
    /// une teinte neutre, ce qui dégrade la finition mais laisse la carte
    /// utilisable. Une exception de ressource, elle, viderait la scène.
    /// </summary>
    private static Brush Ink(string key, byte fallbackAlpha)
        => Application.Current?.Resources?.TryGetValue(key, out object? value) == true && value is Brush brush
            ? brush
            : new SolidColorBrush(Color.FromArgb(fallbackAlpha, 0xFF, 0xFF, 0xFF));

    /// <summary>Lit une valeur des jetons, avec repli sur celle du code.</summary>
    private static double Token(string key, double fallback)
        => Application.Current?.Resources?.TryGetValue(key, out object? value) == true && value is double number
            ? number
            : fallback;

    private static Thickness Token(string key, Thickness fallback)
        => Application.Current?.Resources?.TryGetValue(key, out object? value) == true && value is Thickness thickness
            ? thickness
            : fallback;

    private void OnActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId } || _activityId is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, actionId));
    }

}
