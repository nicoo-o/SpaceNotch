using System;
using System.Collections.Generic;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;

namespace SpaceNotch.Core.Activities;

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

    /// <summary>
    /// Ligne de contexte au-dessus du titre, plus discrète que lui : « Read
    /// app-sidebar.tsx · 219 lines » au-dessus de « Thinking ». Le contexte se lit
    /// d'abord, l'état ensuite. Facultative.
    /// </summary>
    public string? Eyebrow { get; set; }

    /// <summary>
    /// Avancement, de 0 à 1, ou <c>null</c> lorsque l'activité n'a pas de
    /// progression mesurable. Une valeur hors bornes est ramenée dans
    /// l'intervalle par le rendu, jamais rejetée.
    /// </summary>
    public double? Progress { get; set; }

    /// <summary>
    /// Valeur courte affichée à droite de la forme compacte — « 62 % »,
    /// « 12 Mo », « 24:37 », « ✓ ». C'est l'emplacement <em>trailing</em> de la
    /// Dynamic Island : l'identité à gauche, la mesure à droite. Laissée à
    /// <c>null</c>, elle se déduit de <see cref="Progress"/> lorsqu'il existe.
    /// </summary>
    public string? Metric { get; set; }

    /// <summary>Valeur affichée à droite de la forme compacte, déclarée ou déduite.</summary>
    public string? TrailingMetric => Metric ?? (Progress is { } progress
        ? string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{Math.Round(Math.Clamp(progress, 0, 1) * 100):0} %")
        : null);

    /// <summary>
    /// Où en est le travail de l'activité. Décide s'il y a un mouvement
    /// hypnotique, indépendamment de la forme de la notch. Voir ADR-018.
    /// </summary>
    public ActivityMotionState MotionState { get; set; } = ActivityMotionState.Idle;

    /// <summary>
    /// Nature du mouvement demandé pendant le travail : lecture, recherche,
    /// synchronisation… Un greffon demande un préréglage ; il ne dessine jamais
    /// sa propre animation.
    /// </summary>
    public HypnoticPreset MotionPreset { get; set; } = HypnoticPreset.None;

    /// <summary>
    /// Cohabitation avec les autres activités, lorsqu'elle doit être forcée.
    /// Laissée à <c>null</c>, elle se déduit de la priorité et de la durée de vie.
    /// Voir <see cref="ActivityPolicies"/>.
    /// </summary>
    public ActivityPresentationPolicy? Policy { get; init; }

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
