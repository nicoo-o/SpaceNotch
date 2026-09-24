using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Activities;

/// <summary>
/// Charge utile d'un retour visuel système : volume, luminosité, muet.
///
/// Une seule forme pour tous les retours système évite une scène par
/// fonctionnalité — et surtout évite de faire deviner au rendu ce qu'il affiche.
/// </summary>
/// <param name="Value">Valeur courante.</param>
/// <param name="Maximum">Valeur maximale de l'échelle.</param>
/// <param name="Label">Libellé affiché.</param>
/// <param name="ValueText">Valeur mise en forme, prête à afficher.</param>
/// <param name="IconKey">Clé d'icône logique, résolue par le rendu.</param>
public sealed record HudPayload(
    double Value,
    double Maximum,
    string Label,
    string ValueText,
    string? IconKey);

/// <summary>Une notification d'un groupe.</summary>
/// <param name="Title">Titre — souvent l'expéditeur.</param>
/// <param name="Body">Corps du message.</param>
/// <param name="ReceivedAt">Instant de réception.</param>
public sealed record NotificationItem(string Title, string Body, DateTimeOffset ReceivedAt);

/// <summary>
/// Charge utile d'un groupe de notifications d'une même application : la plus
/// récente en tête.
/// </summary>
/// <param name="AppName">Application d'origine.</param>
/// <param name="Items">Notifications, de la plus récente à la plus ancienne.</param>
public sealed record NotificationGroupPayload(string AppName, IReadOnlyList<NotificationItem> Items)
{
    /// <summary>Nombre de notifications du groupe.</summary>
    public int Count => Items.Count;
}

/// <summary>
/// Charge utile d'un minuteur.
/// </summary>
/// <param name="Remaining">Temps restant ou écoulé, selon le mode.</param>
/// <param name="IsRunning">Vrai lorsque le compteur avance.</param>
/// <param name="Mode">Libellé du mode : « Focus », « Minuteur », « Chronomètre ».</param>
public sealed record TimerPayload(
    TimeSpan Remaining,
    bool IsRunning,
    string Mode)
{
    /// <summary>Mise en forme <c>mm:ss</c>, ou <c>h:mm:ss</c> au-delà d'une heure.</summary>
    public string Formatted => Remaining.TotalHours >= 1
        ? Remaining.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
        : Remaining.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Entrée de l'historique du presse-papier.
///
/// Le contenu n'est jamais inclus en entier : seule une prévisualisation courte
/// circule dans le cœur, ce qui borne la mémoire et limite la diffusion de
/// données sensibles.
/// </summary>
/// <param name="Id">Identifiant stable de l'entrée.</param>
/// <param name="Kind">Nature du contenu : texte, lien, image, fichiers.</param>
/// <param name="Preview">Prévisualisation tronquée, jamais le contenu complet.</param>
/// <param name="IsPinned">Vrai si l'entrée est épinglée, donc jamais évincée.</param>
public sealed record ClipboardEntry(
    string Id,
    string Kind,
    string Preview,
    bool IsPinned);

/// <summary>Charge utile de la scène presse-papier.</summary>
public sealed record ClipboardPayload(IReadOnlyList<ClipboardEntry> Entries);

/// <summary>
/// Application proposée par le lanceur.
/// </summary>
/// <param name="Id">Identifiant stable, utilisé comme valeur d'action.</param>
/// <param name="Name">Nom affiché.</param>
/// <param name="Target">Chemin du raccourci à ouvrir.</param>
/// <param name="IsRecent">
/// Vrai si l'application a été lancée depuis l'Island pendant la session.
///
/// Windows n'expose aucun horodatage des applications récemment utilisées : ce
/// qui est affiché ici est donc un historique de session, et il est nommé comme
/// tel plutôt que présenté comme l'historique du système.
/// </param>
public sealed record LauncherEntry(
    string Id,
    string Name,
    string Target,
    bool IsRecent = false);

/// <summary>Charge utile du lanceur d'applications.</summary>
/// <param name="Entries">Applications correspondant à la recherche en cours.</param>
/// <param name="Query">Recherche en cours, vide si aucune.</param>
public sealed record LauncherPayload(
    IReadOnlyList<LauncherEntry> Entries,
    string Query);
