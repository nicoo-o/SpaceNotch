using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpaceNotch.Core.Activities;
using SpaceNotch_App.Views;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Lanceur d'applications : favorites, applications récentes et recherche.
///
/// La recherche est déléguée à la fonctionnalité : la vue transmet la saisie et
/// se contente d'afficher le résultat. Filtrer ici aurait dupliqué la logique de
/// classement — favorites d'abord — dans le rendu.
/// </summary>
public sealed partial class LauncherScene : UserControl, IIslandSceneView
{
    public const string LaunchAction = "launcher.launch";

    public const string SearchAction = "launcher.search";

    private string? _activityId;

    private List<LauncherItem> _items = [];

    private bool _searchFocused;

    private string _query = string.Empty;

    public LauncherScene()
    {
        InitializeComponent();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        _activityId = activity.Id;

        if (activity.Payload is not LauncherPayload payload)
        {
            return;
        }

        // Le champ de recherche n'est réécrit que s'il a divergé, et jamais
        // pendant la frappe : le réécrire replacerait le curseur et ferait sauter
        // la saisie en cours. Le focus est celui de la zone de texte interne,
        // suivi par GotFocus/LostFocus — FocusState de l'AutoSuggestBox, lui,
        // reste « sans focus » quand c'est sa zone de texte qui l'a.
        if (!_searchFocused && !string.Equals(SearchBox.Text, payload.Query, StringComparison.Ordinal))
        {
            SearchBox.Text = payload.Query;
        }

        _query = payload.Query ?? string.Empty;

        List<LauncherItem> items = payload.Entries.Select(LauncherItem.From).ToList();

        // La liste n'est remplacée que si elle a changé : la remplacer à chaque
        // publication réinitialisait la sélection et déplaçait le focus.
        if (!items.SequenceEqual(_items))
        {
            AppsList.ItemsSource = items;
            _items = items;
        }

        // Avec une requête, le premier résultat est présélectionné : c'est lui
        // qu'Entrée lancera. Sans requête, rien n'est présélectionné.
        if (_items.Count > 0 && _query.Length > 0 && AppsList.SelectedIndex < 0)
        {
            AppsList.SelectedIndex = 0;
        }
        else if (_query.Length == 0)
        {
            AppsList.SelectedIndex = -1;
        }

        bool empty = items.Count == 0;

        AppsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        SectionText.Text = string.IsNullOrWhiteSpace(payload.Query) ? "Récents" : "Résultats";
        SectionText.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Seule la saisie de l'utilisateur nous intéresse : sans ce filtre, le
        // texte réinjecté par Apply relancerait une recherche en boucle.
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        Raise(SearchAction, sender.Text);
    }

    /// <summary>Met le focus dans le champ : on ouvre le lanceur pour taper.</summary>
    public void FocusSearch()
    {
        SearchBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Vrai tant que l'utilisateur tape dans le champ.</summary>
    public bool IsEditing => _searchFocused;

    private void OnSearchGotFocus(object sender, RoutedEventArgs e) => _searchFocused = true;

    private void OnSearchLostFocus(object sender, RoutedEventArgs e) => _searchFocused = false;

    /// <summary>
    /// Entrée lance la ligne sélectionnée. Sans sélection et sans requête, rien
    /// n'est lancé : Entrée dans un champ vide lançait la première application
    /// de la liste alphabétique — un outil de maintenance d'AutoCAD, par exemple.
    /// </summary>
    private void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (AppsList.SelectedItem is LauncherItem selected)
        {
            Raise(LaunchAction, selected.Target);
        }
        else if (_query.Length > 0 && _items.Count > 0)
        {
            Raise(LaunchAction, _items[0].Target);
        }
    }

    /// <summary>
    /// Clavier du champ : ↑/↓ (et Tab) parcourent la liste sans quitter le
    /// champ, Échap vide la requête avant de refermer. Les touches traitées ici
    /// ne remontent pas jusqu'à la notch, qui les prendrait pour les siennes.
    /// </summary>
    private void OnSearchPreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case global::Windows.System.VirtualKey.Down:
                Move(1);
                e.Handled = true;
                break;

            case global::Windows.System.VirtualKey.Up:
                Move(-1);
                e.Handled = true;
                break;

            case global::Windows.System.VirtualKey.Escape when SearchBox.Text.Length > 0:
                SearchBox.Text = string.Empty;
                Raise(SearchAction, string.Empty);
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (_items.Count == 0)
        {
            return;
        }

        int index = AppsList.SelectedIndex < 0
            ? (delta > 0 ? 0 : _items.Count - 1)
            : Math.Clamp(AppsList.SelectedIndex + delta, 0, _items.Count - 1);

        AppsList.SelectedIndex = index;
        AppsList.ScrollIntoView(_items[index]);
    }

    /// <summary>Entrée sur un élément de la liste : il est lancé.</summary>
    private void OnListKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter && AppsList.SelectedItem is LauncherItem item)
        {
            e.Handled = true;
            Raise(LaunchAction, item.Target);
        }
    }

    private void OnAppInvoked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LauncherItem item)
        {
            Raise(LaunchAction, item.Target);
        }
    }

    private void Raise(string actionId, string? value = null)
    {
        if (_activityId is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, actionId, value));
    }

    /// <summary>
    /// Projection d'une entrée du lanceur vers ce que la grille affiche.
    ///
    /// Le type est public et la marque est un caractère : la grille se lie par
    /// réflexion, et une étoile textuelle évite d'avoir à rendre une icône dont
    /// la visibilité dépendrait d'un booléen.
    /// </summary>
    public sealed record LauncherItem(string Name, string Target, string FavoriteMark)
    {
        public static LauncherItem From(LauncherEntry entry)
            => new(entry.Name, entry.Target, entry.IsRecent ? "\u2605" : string.Empty);
    }
}
