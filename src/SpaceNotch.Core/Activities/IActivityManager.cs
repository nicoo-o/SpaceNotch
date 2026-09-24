using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Activities;

/// <summary>
/// Arbitre central des activités, et propriétaire de leur cycle de vie.
///
/// Les fonctionnalités publient et cessent de s'en soucier : c'est le
/// gestionnaire qui décide laquelle est présentée, laquelle expire et laquelle
/// est évincée. Aucun composant ne peut laisser une activité s'accumuler
/// indéfiniment.
/// </summary>
public interface IActivityManager
{
    /// <summary>Activité actuellement la plus prioritaire, ou <c>null</c>.</summary>
    IslandActivity? CurrentActivity { get; }

    /// <summary>Nombre d'activités vivantes, utile aux diagnostics.</summary>
    int Count { get; }

    /// <summary>
    /// Déclenché lorsque l'activité présentée change, y compris lorsqu'elle est
    /// republiée avec un contenu actualisé.
    /// </summary>
    event EventHandler<IslandActivity?>? ActiveActivityChanged;

    /// <summary>
    /// Déclenché pour chaque activité retirée, quelle qu'en soit la raison :
    /// retrait explicite, expiration ou éviction par le plafond d'arrière-plan.
    /// </summary>
    event EventHandler<IslandActivity>? ActivityRemoved;

    /// <summary>
    /// Publie une activité. Si une activité portant le même identifiant existe,
    /// elle est remplacée.
    /// </summary>
    void PostActivity(IslandActivity activity);

    /// <summary>Retire une activité. Retourne <c>true</c> si elle existait.</summary>
    bool RemoveActivity(string activityId);

    /// <summary>Retire toutes les activités d'une fonctionnalité.</summary>
    int RemoveActivitiesFrom(string featureId);

    /// <summary>Activités vivantes, triées par priorité décroissante.</summary>
    IReadOnlyList<IslandActivity> GetActiveActivities();

    /// <summary>
    /// Épingle l'activité présentée, ou <c>null</c> pour rendre la main à
    /// l'arbitrage automatique. Un choix explicite de l'utilisateur prime sur la
    /// priorité tant que l'activité existe.
    /// </summary>
    void PinPresentation(string? activityId);

    /// <summary>
    /// Parcourt la pile d'activités, élément suivant (<c>1</c>) ou précédent
    /// (<c>−1</c>).
    /// </summary>
    /// <returns><c>false</c> si la pile est trop courte pour être parcourue.</returns>
    bool CyclePresentation(int delta);

    /// <summary>
    /// Retire les activités dont la durée de vie est écoulée. Retourne le nombre
    /// d'activités effectivement retirées.
    /// </summary>
    int ExpireOverdue(DateTimeOffset now);

    /// <summary>
    /// Délai restant avant la prochaine expiration, ou <c>null</c> s'il n'y a
    /// rien à faire expirer.
    ///
    /// Permet à l'hôte d'armer un unique minuteur borné au lieu de vérifier
    /// périodiquement — et de le désarmer dès qu'il n'y a plus d'échéance.
    /// </summary>
    TimeSpan? GetTimeUntilNextExpiration(DateTimeOffset now);
}
