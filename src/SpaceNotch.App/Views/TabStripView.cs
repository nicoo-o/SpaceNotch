using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
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
    private readonly List<(string Id, TabButton Tab)> _items = [];

    /// <summary>
    /// Les boutons de la bande, dans l'arbre, dans l'ordre : les premiers
    /// portent les onglets (<see cref="_items"/>), les autres sont repliés.
    /// </summary>
    private readonly List<TabButton> _slots = [];

    /// <summary>
    /// L'activité de chaque emplacement (nulle s'il est replié). Pas dans
    /// <c>Tag</c> : le gabarit des boutons y lit son encre de survol.
    /// </summary>
    private readonly List<string?> _slotIds = [];

    private string? _selected;

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

    /// <summary>Affiche les onglets ; <paramref name="activities"/> vide replie la bande.</summary>
    public void Show(IReadOnlyList<IslandActivity> activities, string? selectedId, Brush? text, Brush? dim)
    {
        ArgumentNullException.ThrowIfNull(activities);
        Visibility = activities.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;

        // Les boutons restent dans l'arbre et sont réutilisés par position : y
        // entrer et en sortir coûtait 4 à 8 ms par rendu (n° 46b, visites du
        // 2026-10-18), avant cela, reconstruits à chaque rendu, ils faisaient
        // provoquer à XAML des GC de génération 2 de 11 à 20 ms.
        Sync(activities, text);

        bool changed = !string.Equals(selectedId, _selected, StringComparison.Ordinal);
        _selected = selectedId;

        // Seul l'onglet choisi porte son titre ; les autres, leur icône. La
        // rangée tient ainsi dans la notch quel que soit le nombre d'activités.
        foreach ((string id, TabButton tab) in _items)
        {
            bool chosen = string.Equals(id, selectedId, StringComparison.Ordinal);
            tab.SetChosen(chosen);

            if (tab.Content is StackPanel content && content.Children.Count > 1 && content.Children[1] is TextBlock label)
            {
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

    /// <summary>Les onglets affichés deviennent ceux de <paramref name="activities"/>, dans leur ordre.</summary>
    private void Sync(IReadOnlyList<IslandActivity> activities, Brush? text)
    {
        _items.Clear();

        for (int i = 0; i < activities.Count; i++)
        {
            IslandActivity activity = activities[i];

            if (i == _slots.Count)
            {
                TabButton created = CreateTab();
                _tabs.Children.Add(created);
                _slots.Add(created);
                _slotIds.Add(null);
            }

            TabButton tab = _slots[i];

            // Un onglet qui a le focus ou qu'on est en train d'appuyer ne change
            // jamais d'activité sous la main : il est remplacé par un neuf, et
            // perd le focus ou l'appui comme quand la bande était reconstruite.
            if (_slotIds[i] is { } previous
                && !string.Equals(previous, activity.Id, StringComparison.Ordinal)
                && (tab.IsPressed || tab.PointerCaptures is { Count: > 0 } || tab.FocusState != FocusState.Unfocused))
            {
                tab = CreateTab();
                _tabs.Children[i] = tab;
                _slots[i] = tab;
            }

            _slotIds[i] = activity.Id;
            tab.Visibility = Visibility.Visible;

            // Le Narrateur dit « onglet 2 sur 3 » (n° 104).
            AutomationProperties.SetPositionInSet(tab, i + 1);
            AutomationProperties.SetSizeOfSet(tab, activities.Count);
            _items.Add((activity.Id, tab));

            if (tab.Content is not StackPanel { Children.Count: > 1 } content)
            {
                continue;
            }

            // Une autre activité à cette place : son icône remplace la précédente
            // en faisant voyager les pixels (P6), comme dans la notch.
            if (content.Children[0] is GlyphView glyph)
            {
                if (!string.Equals(glyph.Key, activity.IconKey, StringComparison.Ordinal))
                {
                    glyph.Key = activity.IconKey;
                }

                // Le thème ou le contraste a pu changer : le pinceau du titre est déjà reposé à chaque passage.
                if (!ReferenceEquals(glyph.Tint, text))
                {
                    glyph.Tint = text;
                }
            }

            if (content.Children[1] is TextBlock label && !string.Equals(label.Text, activity.Title, StringComparison.Ordinal))
            {
                label.Text = activity.Title;
                AutomationProperties.SetName(tab, activity.Title);
            }
        }

        // Repliés, ils n'ont plus d'activité : un clic ou Entrée n'y peut plus rien ouvrir.
        for (int i = activities.Count; i < _slots.Count; i++)
        {
            _slots[i].Visibility = Visibility.Collapsed;
            _slotIds[i] = null;
        }
    }

    private TabButton CreateTab()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new GlyphView { Size = 12, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock
        {
            FontSize = 11.5,
            MaxWidth = 140,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        var tab = new TabButton
        {
            Content = content,
            Height = TabHeight,
            MinHeight = 0,
            Padding = new Thickness(10, 0, 10, 0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),

            // Un clic ne prend pas le focus à la scène (une note en cours de
            // frappe) ; le clavier atteint toujours les onglets par Tab.
            AllowFocusOnInteraction = false
        };

        if (Application.Current.Resources.TryGetValue("NfGhostButtonStyle", out object? style) && style is Style ghost)
        {
            tab.Style = ghost;
        }

        // L'activité est lue au clic : elle change quand le bouton est réutilisé,
        // mais jamais pendant un appui (voir Sync).
        tab.Click += (sender, _) =>
        {
            int slot = sender is TabButton clicked ? _slots.IndexOf(clicked) : -1;

            if (slot >= 0 && _slotIds[slot] is { } id)
            {
                TabInvoked?.Invoke(this, id);
            }
        };

        return tab;
    }

    /// <summary>
    /// Un onglet pour UI Automation (n° 104) : le Narrateur le dit « onglet »,
    /// dit lequel est choisi, et peut le choisir. Les 24 DIP de haut restent :
    /// c'est le minimum des WCAG 2.2 (2.5.8), et la rangée n'a pas la place
    /// d'en montrer 32 sans grandir la notch ouverte.
    /// </summary>
    private sealed partial class TabButton : Button
    {
        public bool IsChosen { get; private set; }

        public void SetChosen(bool chosen)
        {
            if (chosen == IsChosen)
            {
                return;
            }

            IsChosen = chosen;

            if (AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged)
                && FrameworkElementAutomationPeer.FromElement(this) is { } peer)
            {
                peer.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, !chosen, chosen);
            }

            if (chosen
                && AutomationPeer.ListenerExists(AutomationEvents.SelectionItemPatternOnElementSelected)
                && FrameworkElementAutomationPeer.FromElement(this) is { } selected)
            {
                selected.RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);
            }
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new TabButtonPeer(this);
    }

    private sealed partial class TabButtonPeer : ButtonAutomationPeer, ISelectionItemProvider
    {
        private readonly TabButton _owner;

        public TabButtonPeer(TabButton owner)
            : base(owner) => _owner = owner;

        public bool IsSelected => _owner.IsChosen;

        public IRawElementProviderSimple? SelectionContainer => null;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;

        protected override object GetPatternCore(PatternInterface patternInterface)
            => patternInterface == PatternInterface.SelectionItem ? this : base.GetPatternCore(patternInterface);

        /// <summary>Choisir l'onglet, c'est le cliquer : la notch fait le reste.</summary>
        public void Select() => Invoke();

        public void AddToSelection() => Select();

        /// <summary>Un onglet reste toujours choisi : rien à retirer.</summary>
        public void RemoveFromSelection()
        {
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

        foreach ((string id, TabButton tab) in _items)
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
