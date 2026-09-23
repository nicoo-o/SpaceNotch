namespace NotchFlow.Core.Activities;

/// <summary>
/// Demande d'exécution d'une action déclarée par une fonctionnalité.
///
/// La vue ne connaît que des identifiants d'action ; elle ne sait pas ce qu'ils
/// font. La demande traverse donc le cœur jusqu'à la fonctionnalité propriétaire,
/// qui seule détient le moyen de l'exécuter. C'est ce qui permet d'ajouter un
/// contrôle — un bouton de transport, une piste à déplacer — sans que le rendu
/// n'ait à connaître le moindre type de contenu.
/// </summary>
/// <param name="ActivityId">Activité à l'origine de la demande.</param>
/// <param name="ActionId">Identifiant de l'action déclarée, par exemple <c>media.next</c>.</param>
/// <param name="Value">
/// Valeur associée, exprimée sous forme de texte neutre. Une action continue —
/// une position de lecture déplacée — transporte ici sa cible. Le cœur reste donc
/// sans dépendance à un type de média particulier.
/// </param>
public sealed record IslandActionRequest(string ActivityId, string ActionId, string? Value = null);
