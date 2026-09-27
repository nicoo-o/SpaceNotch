using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;

namespace SpaceNotch_App.Views;

/// <summary>
/// Onglets glissants (U2) : quand plusieurs activités tournent, la notch
/// ouverte les range en onglets. Une pilule glisse de l'un à l'autre sur un
/// ressort ; le contenu change sous le fondu en pixels.
/// </summary>
public sealed partial class TabStripView : Grid
{
    /// <summary>Hauteur de la rangée, marge comprise, en DIPs.</summary>
    public const double RowHeight = 34;

    private const double TabHeight = 24;

    private readonly Border _pill = new() { Height = TabHeight, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly List<(string Id, Button Tab)> _items = [];
    private string? _selected;
    private string _signature = string.Empty;

    public TabStripView()
    {
        Height = TabHeight;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Visibility = Visibility.Collapsed;
        Children.Add(_pill);
        Children.Add(_tabs);
        _tabs.LayoutUpdated += (_, _) => MovePill(animate: _slideNext, onlyIfUnplaced: true);
    }

    /// <summary>Un onglet est choisi : l'identifiant de son activité.</summary>
    public event EventHandler<string>? TabInvoked;

    /// <summary>
    /// Pinceau de la pilule de sélection. Propriété de dépendance : le XAML le
    /// donne par <c>{ThemeResource}</c>.
    /// </summary>
    public static readonly DependencyProperty PillBrushProperty = DependencyProperty.Register(
        nameof(PillBrush), typeof(Brush), typeof(TabStripView), new PropertyMetadata(null, (d, e) => ((TabStripView)d)._pill.Background = (Brush?)e.NewValue));

    /// <summary>Pinceau de la pilule de sélection.</summary>
    public Brush? PillBrush
    {
        get => (Brush?)GetValue(PillBrushProperty);
        set => SetValue(PillBrushProperty, value);
    }

    /// <summary>Faux quand Windows réduit les animations : la pilule saute.</summary>
    public bool Animate { get; set; } = true;

    /// <summary>Affiche les onglets ; <paramref name="activities"/> vide les retire.</summary>
    public void Show(IReadOnlyList<IslandActivity> activities, string? selectedId, Brush? text, Brush? dim)
    {
        ArgumentNullException.ThrowIfNull(activities);
        Visibility = activities.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;

        string signature = string.Join('|', activities.Select(a => a.Id + "·" + a.IconKey + "·" + a.Title));

        if (!string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            _signature = signature;
            Rebuild(activities, text);
        }

        bool changed = !string.Equals(selectedId, _selected, StringComparison.Ordinal);
        _selected = selectedId;

        // Seul l'onglet choisi porte son titre ; les autres, leur icône. La
        // rangée tient ainsi dans la notch quel que soit le nombre d'activités.
        foreach ((string id, Button tab) in _items)
        {
            if (tab.Content is StackPanel content && content.Children.Count > 1 && content.Children[1] is TextBlock label)
            {
                bool chosen = string.Equals(id, selectedId, StringComparison.Ordinal);
                label.Foreground = chosen ? text : dim;
                label.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
                content.Children[0].Opacity = chosen ? 1 : 0.55;
            }
        }

        // Les largeurs changent avec le titre montré : la pilule se place après
        // la mise en page, sur le ressort si l'onglet a changé.
        _slideNext = changed && Animate;
        _placed = false;
    }

    private void Rebuild(IReadOnlyList<IslandActivity> activities, Brush? text)
    {
        _tabs.Children.Clear();
        _items.Clear();

        foreach (IslandActivity activity in activities)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new GlyphView { Key = activity.IconKey, Size = 12, Tint = text, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock
            {
                Text = activity.Title,
                FontSize = 11.5,
                MaxWidth = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            var tab = new Button
            {
                Content = content,
                Height = TabHeight,
                MinHeight = 0,
                Padding = new Thickness(10, 0, 10, 0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(12)
            };

            if (Application.Current.Resources.TryGetValue("NfGhostButtonStyle", out object? style) && style is Style ghost)
            {
                tab.Style = ghost;
            }

            string id = activity.Id;
            tab.Click += (_, _) => TabInvoked?.Invoke(this, id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tab, activity.Title);

            _tabs.Children.Add(tab);
            _items.Add((id, tab));
        }
    }

    private bool _placed;

    /// <summary>La prochaine mise en place de la pilule glisse (changement d'onglet) plutôt que de sauter.</summary>
    private bool _slideNext;

    /// <summary>La pilule rejoint l'onglet choisi, sur un ressort.</summary>
    private void MovePill(bool animate, bool onlyIfUnplaced)
    {
        if (onlyIfUnplaced && _placed)
        {
            return;
        }

        foreach ((string id, Button tab) in _items)
        {
            if (!string.Equals(id, _selected, StringComparison.Ordinal) || tab.ActualWidth <= 0)
            {
                continue;
            }

            _placed = true;
            double x = tab.TransformToVisual(_tabs).TransformPoint(default).X;
            _pill.Width = tab.ActualWidth;

            Visual visual = ElementCompositionPreview.GetElementVisual(_pill);
            ElementCompositionPreview.SetIsTranslationEnabled(_pill, true);
            var target = new Vector3((float)x, 0, 0);

            if (!animate)
            {
                visual.Properties.InsertVector3("Translation", target);
                return;
            }

            SpringVector3NaturalMotionAnimation slide = visual.Compositor.CreateSpringVector3Animation();
            slide.FinalValue = target;
            slide.DampingRatio = 0.72f;
            slide.Period = TimeSpan.FromMilliseconds(50);
            visual.StartAnimation("Translation", slide);
            return;
        }
    }
}
