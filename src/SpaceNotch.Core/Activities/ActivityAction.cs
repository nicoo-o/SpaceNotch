namespace SpaceNotch.Core.Activities;

/// <summary>
/// Nature d'une action exposée par une activité.
/// </summary>
public enum ActivityActionKind
{
    /// <summary>Bascule sans état binaire exposé (lecture/pause, muet).</summary>
    Toggle,

    /// <summary>Déclenchement ponctuel (piste suivante, fermeture).</summary>
    Invoke,

    /// <summary>Ouvre une ressource externe (fichier, URL, application).</summary>
    Open
}

/// <summary>
/// Action déclarée par une fonctionnalité et rendue par l'Island.
///
/// L'interface ne connaît aucun cas particulier : elle affiche les actions
/// fournies et renvoie l'identifiant de celle qui a été activée. C'est ce qui
/// permet à une fonctionnalité tierce d'ajouter ses propres contrôles sans
/// modifier le rendu.
/// </summary>
public sealed record ActivityAction(
    string Id,
    string Label,
    string IconKey,
    ActivityActionKind Kind = ActivityActionKind.Invoke,
    bool IsPrimary = false,
    bool IsEnabled = true);
