using System;
using Microsoft.UI.Xaml;
using SpaceNotch.Core.Activities;

namespace SpaceNotch_App.Views;

/// <summary>
/// Contrat d'une scène de l'Island.
///
/// La fenêtre ne connaît que ce contrat : elle associe une clé de scène déclarée
/// par la fonctionnalité à une vue, puis délègue. Chaque vue interprète son
/// propre contenu, ce qui évite toute chaîne de conditions sur le type reçu dans
/// la fenêtre — et permet d'ajouter une scène sans la modifier.
/// </summary>
public interface IIslandSceneView
{
    /// <summary>Élément racine de la scène, activé ou masqué par la fenêtre.</summary>
    FrameworkElement Root { get; }

    /// <summary>
    /// Demande d'exécution d'une action déclarée par l'activité présentée.
    ///
    /// Une scène n'exécute rien elle-même : elle transmet l'identifiant de
    /// l'action, que la fenêtre route vers la fonctionnalité propriétaire. C'est
    /// ce qui permet à une vue de proposer un contrôle sans connaître le service
    /// qui le réalisera.
    ///
    /// L'implémentation par défaut ignore l'abonnement : toutes les scènes ne
    /// proposent pas de contrôle — une notification informe, elle n'agit pas — et
    /// les obliger à déclarer un événement jamais déclenché serait du bruit. Une
    /// scène qui propose des contrôles redéclare l'événement.
    /// </summary>
    event EventHandler<IslandActionRequest>? ActionRequested
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Projette l'activité dans la vue. Appelée à chaque republication, y compris
    /// lorsque le contenu évolue sans changer de scène.
    /// </summary>
    void Apply(IslandActivity activity);
}
