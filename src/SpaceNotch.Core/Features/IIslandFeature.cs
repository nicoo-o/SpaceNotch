using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;

namespace SpaceNotch.Core.Features;

/// <summary>
/// État du cycle de vie d'une fonctionnalité.
/// </summary>
public enum FeatureState
{
    /// <summary>Inactive : aucune ressource acquise, aucun travail en attente.</summary>
    Stopped,

    Starting,

    /// <summary>Active et à l'écoute des événements système.</summary>
    Running,

    Stopping,

    /// <summary>L'activation a échoué. La fonctionnalité n'effectue plus rien.</summary>
    Faulted
}

/// <summary>
/// Signal de changement d'état d'une fonctionnalité.
/// </summary>
/// <remarks>
/// Classe dérivée de <see cref="EventArgs"/>, par cohérence avec
/// <c>IslandStateChangedEventArgs</c> : le suffixe est réservé par convention aux
/// charges utiles d'événement, et les diagnostics s'appuient dessus.
/// </remarks>
public sealed class FeatureStateChangedEventArgs : EventArgs
{
    public FeatureStateChangedEventArgs(
        string featureId,
        FeatureState oldState,
        FeatureState newState,
        Exception? error)
    {
        FeatureId = featureId;
        OldState = oldState;
        NewState = newState;
        Error = error;
    }

    public string FeatureId { get; }

    public FeatureState OldState { get; }

    public FeatureState NewState { get; }

    public Exception? Error { get; }
}

/// <summary>
/// Contrat d'une fonctionnalité de l'Island.
///
/// Le cycle de vie est explicite parce que le cahier des charges impose
/// « fonctionnalité inactive = zéro travail » : une fonctionnalité arrêtée doit
/// avoir libéré ses écouteurs système, pas simplement cessé d'afficher ses
/// activités. C'est aussi ce qui rend possible une bascule d'activation
/// réellement effective, et non un simple masquage.
/// </summary>
public interface IIslandFeature : IAsyncDisposable
{
    /// <summary>Identifiant stable, utilisé comme propriétaire des activités.</summary>
    string Id { get; }

    /// <summary>Nom lisible, destiné aux réglages et aux diagnostics.</summary>
    string DisplayName { get; }

    FeatureState State { get; }

    /// <summary>Préférence effective : une fonctionnalité désactivée ne démarre pas.</summary>
    bool IsEnabled { get; }

    /// <summary>Dernière erreur rencontrée, conservée pour les diagnostics.</summary>
    Exception? LastError { get; }

    /// <summary>Signalé à chaque transition d'état.</summary>
    event EventHandler<FeatureStateChangedEventArgs>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    /// <summary>
    /// Active ou désactive la fonctionnalité. Désactiver libère immédiatement les
    /// ressources système et retire les activités publiées ; activer réacquiert
    /// les écouteurs. L'opération est idempotente.
    /// </summary>
    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Activités actuellement publiées par cette fonctionnalité.</summary>
    IEnumerable<IslandActivity> GetActivities();

    /// <summary>
    /// Exécute une action déclarée par l'une de ses activités.
    ///
    /// Seule la fonctionnalité sait ce que l'action signifie ; la vue transmet un
    /// identifiant et rien d'autre.
    /// </summary>
    /// <returns><c>true</c> si l'action a été reconnue et exécutée.</returns>
    Task<bool> HandleActionAsync(IslandActionRequest request);
}
