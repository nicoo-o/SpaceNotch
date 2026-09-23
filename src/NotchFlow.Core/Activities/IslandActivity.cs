using System;
using System.Collections.Generic;
using NotchFlow.Core.Scenes;
using NotchFlow.Core.State;

namespace NotchFlow.Core.Activities;

/// <summary>
/// Une unité d'information contextuelle affichable dans l'Island.
///
/// Le modèle porte tout ce dont le rendu a besoin : la fonctionnalité
/// propriétaire, la nature de l'information, la scène à présenter et les actions
/// disponibles. Rien de tout cela n'est décidé par l'interface.
/// </summary>
public sealed class IslandActivity
{
    /// <summary>
    /// Identifiant stable de l'activité. Une activité republiée sous le même
    /// identifiant <em>remplace</em> la précédente au lieu de s'y ajouter : c'est
    /// ce qui empêche l'accumulation d'entrées à chaque changement de volume ou
    /// chaque notification.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Identifiant de la fonctionnalité propriétaire, utilisé pour interroger
    /// les activités d'une fonctionnalité donnée et pour la désactiver.
    /// </summary>
    public required string FeatureId { get; init; }

    /// <summary>
    /// Clé de scène déclarée par la fonctionnalité. Voir
    /// <see cref="IslandSceneCatalog"/> : la fenêtre résout cette clé en vue et
    /// en dimensions.
    /// </summary>
    public required string SceneKey { get; init; }

    public required string Title { get; set; }

    public string? Subtitle { get; set; }

    /// <summary>Clé d'icône logique, résolue par le jeu d'icônes du rendu.</summary>
    public string? IconKey { get; set; }

    /// <summary>Application à l'origine du contenu (Spotify, Discord, Système…).</summary>
    public string? Source { get; set; }

    /// <summary>Nature contextuelle de l'activité.</summary>
    public IslandActivityState State { get; set; } = IslandActivityState.Idle;

    public ActivityPriority Priority { get; set; } = ActivityPriority.Normal;

    /// <summary>
    /// Palier de présentation au repos, lorsqu'il doit être forcé.
    ///
    /// Laissé à <c>null</c>, le palier se déduit de <see cref="Priority"/> — un
    /// téléchargement d'arrière-plan reste un signal, un appel entrant devient une
    /// carte. Cette surcharge existe pour les cas où les deux divergent
    /// légitimement : un transfert de plusieurs heures est peu urgent mais mérite
    /// d'être lu. Voir <see cref="IslandPresentation"/>.
    /// </summary>
    public IslandPresentationTier? Presentation { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Durée de vie de l'activité. Une fois écoulée, le gestionnaire la retire
    /// automatiquement : les fonctionnalités n'ont plus à orchestrer leur propre
    /// expiration.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Contrôles exposés par la fonctionnalité, rendus tels quels par l'Island.
    /// </summary>
    public IReadOnlyList<ActivityAction> Actions { get; init; } = [];

    /// <summary>
    /// Teinte d'ambiance que l'activité souhaite donner à l'atmosphère de
    /// l'Island.
    ///
    /// Elle est déclarée, jamais devinée par le rendu : la fonctionnalité média,
    /// par exemple, la déduit de la couleur dominante de la pochette. Laissée à
    /// <c>null</c>, l'atmosphère conserve sa teinte de référence.
    /// </summary>
    public ActivityTint? Tint { get; init; }

    /// <summary>
    /// Données propres à la scène, consommées par la vue correspondante.
    /// </summary>
    public object? Payload { get; set; }

    /// <summary>Échéance absolue, ou <c>null</c> si l'activité est persistante.</summary>
    public DateTimeOffset? ExpiresAt => Duration is { } duration ? CreatedAt + duration : null;

    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAt is { } expiresAt && expiresAt <= now;

    /// <summary>
    /// Encombrement de l'Island lorsque cette activité est présentée.
    /// </summary>
    public IslandFootprint Footprint => IslandSceneCatalog.FootprintFor(SceneKey);
}
