using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotchFlow.Core.Activities;
using NotchFlow_App.Views;

namespace NotchFlow_App.Views.Scenes;

/// <summary>
/// Historique du presse-papier.
///
/// La scène affiche et transmet ; elle ne détient aucune donnée. Coller, épingler
/// et supprimer sont trois demandes d'action, exécutées par la fonctionnalité
/// propriétaire — seule détentrice du contenu réel.
/// </summary>
public sealed partial class ClipboardScene : UserControl, IIslandSceneView
{
    public const string PasteAction = "clipboard.paste";

    public const string PinAction = "clipboard.pin";

    public const string DeleteAction = "clipboard.remove";

    private string? _activityId;

    public ClipboardScene()
    {
        InitializeComponent();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        _activityId = activity.Id;

        if (activity.Payload is not ClipboardPayload payload)
        {
            return;
        }

        EntriesListView.ItemsSource = payload.Entries;
        CountText.Text = $"{payload.Entries.Count} élément{(payload.Entries.Count > 1 ? "s" : string.Empty)}";

        bool empty = payload.Entries.Count == 0;

        EntriesListView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPasteClicked(object sender, RoutedEventArgs e)
        => RaiseFor(sender, PasteAction);

    private void OnPinClicked(object sender, RoutedEventArgs e)
        => RaiseFor(sender, PinAction);

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
        => RaiseFor(sender, DeleteAction);

    /// <summary>
    /// Retrouve l'entrée visée par le contexte de données de la ligne : c'est
    /// l'identifiant de l'entrée, jamais son contenu, qui est transmis.
    /// </summary>
    private void RaiseFor(object sender, string actionId)
    {
        if (_activityId is null)
        {
            return;
        }

        if (sender is not FrameworkElement { DataContext: ClipboardEntry entry })
        {
            return;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, actionId, entry.Id));
    }
}
