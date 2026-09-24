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
        // la saisie en cours.
        bool editing = SearchBox.FocusState != FocusState.Unfocused;

        if (!editing && !string.Equals(SearchBox.Text, payload.Query, StringComparison.Ordinal))
        {
            SearchBox.Text = payload.Query;
        }

        List<LauncherItem> items = payload.Entries.Select(LauncherItem.From).ToList();

        // La liste est reconstruite à chaque publication : la grille reste donc
        // exactement le reflet du catalogue de la fonctionnalité.

        AppsGridView.ItemsSource = items;

        bool empty = items.Count == 0;

        AppsGridView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
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
