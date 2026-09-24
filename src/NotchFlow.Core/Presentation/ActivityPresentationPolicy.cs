using System;
using NotchFlow.Core.Activities;

namespace NotchFlow.Core.Presentation;

/// <summary>
/// Comment une activité cohabite avec les autres.
///
/// <para>
/// Une fonctionnalité ne doit pas forcément remplacer l'activité en cours :
/// Spotify, puis « Volume 72 % », puis Spotify de nouveau — la musique n'a
/// jamais disparu, elle a été brièvement recouverte. La politique dit ce qu'une
/// activité est pour les autres, indépendamment de son urgence.
/// </para>
/// </summary>
public enum ActivityPresentationPolicy
{
    /// <summary>Présente tant que sa source l'est : une lecture média.</summary>
    Persistent = 0,

    /// <summary>Présente mais discrète : un téléchargement, une synchronisation.</summary>
    Passive = 1,

    /// <summary>Un retour bref qui recouvre puis rend la main : volume, luminosité.</summary>
    Temporary = 2,

    /// <summary>Réclame l'attention tout de suite : un appel entrant.</summary>
    Interrupting = 3
}

/// <summary>
/// Décision prise à l'arrivée d'une nouvelle activité.
/// </summary>
public enum ActivityInterruption
{
    /// <summary>Rien ne change à l'écran.</summary>
    Ignore = 0,

    /// <summary>L'activité attend : l'utilisateur regarde autre chose, on ne le lui retire pas.</summary>
    Queue = 1,

    /// <summary>L'activité recouvre brièvement la présentation courante, qui reviendra seule.</summary>
    Overlay = 2,

    /// <summary>L'activité prend la place et ouvre la notch.</summary>
    Interrupt = 3,

    /// <summary>L'activité prend la place, sans ouvrir.</summary>
    Replace = 4
}

/// <summary>
/// Règles de cohabitation des activités.
/// </summary>
public static class ActivityPolicies
{
    /// <summary>
    /// Politique d'une activité : celle qu'elle déclare, sinon celle que son
    /// urgence et sa durée de vie impliquent.
    ///
    /// La déduction évite à chaque fonctionnalité de se décrire deux fois : un
    /// retour qui expire seul est temporaire, une urgence critique interrompt, une
    /// activité d'arrière-plan sans échéance persiste.
    /// </summary>
    public static ActivityPresentationPolicy Resolve(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (activity.Policy is { } declared)
        {
            return declared;
        }

        if (activity.Priority == ActivityPriority.Critical)
        {
            return ActivityPresentationPolicy.Interrupting;
        }

        if (activity.Duration is not null)
        {
            return ActivityPresentationPolicy.Temporary;
        }

        return activity.Priority == ActivityPriority.Background
            ? ActivityPresentationPolicy.Persistent
            : ActivityPresentationPolicy.Passive;
    }

    /// <summary>
    /// Décide de ce que devient la présentation quand <paramref name="incoming"/>
    /// arrive alors que <paramref name="current"/> est présentée.
    /// </summary>
    /// <param name="current">Activité présentée, ou <c>null</c>.</param>
    /// <param name="incoming">Activité qui vient d'arriver ou d'être republiée.</param>
    /// <param name="presentation">Ce que la notch montre au moment de l'arrivée.</param>
    public static ActivityInterruption Decide(
        IslandActivity? current,
        IslandActivity? incoming,
        NotchPresentation presentation)
    {
        if (incoming is null)
        {
            return ActivityInterruption.Ignore;
        }

        // Une republication est une mise à jour : elle remplace sans rien
        // réclamer, quelle que soit sa politique.
        if (current is null || string.Equals(current.Id, incoming.Id, StringComparison.Ordinal))
        {
            return current is null && Resolve(incoming) == ActivityPresentationPolicy.Interrupting
                ? ActivityInterruption.Interrupt
                : ActivityInterruption.Replace;
        }

        ActivityPresentationPolicy policy = Resolve(incoming);

        if (policy == ActivityPresentationPolicy.Interrupting)
        {
            return ActivityInterruption.Interrupt;
        }

        // L'utilisateur a ouvert la notch pour regarder quelque chose : on ne
        // remplace pas ce contenu sous son pointeur par une information moins
        // urgente. Elle attend la fermeture.
        if (presentation == NotchPresentation.Expanded && incoming.Priority < ActivityPriority.High)
        {
            return ActivityInterruption.Queue;
        }

        if (policy == ActivityPresentationPolicy.Temporary)
        {
            return ActivityInterruption.Overlay;
        }

        return incoming.Priority >= current.Priority
            ? ActivityInterruption.Replace
            : ActivityInterruption.Queue;
    }
}
