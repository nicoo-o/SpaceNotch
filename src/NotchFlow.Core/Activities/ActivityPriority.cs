namespace NotchFlow.Core.Activities;

/// <summary>
/// Niveaux de priorité pour l'arbitrage des activités affichées dans l'Island.
/// </summary>
public enum ActivityPriority
{
    /// <summary>
    /// Activités d'arrière-plan (ex: musique Spotify, minuteur passif).
    /// </summary>
    Background = 0,

    /// <summary>
    /// Activités standards (ex: téléchargement terminé, connexion périphérique).
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Activités importantes (ex: batterie faible 10%, rappel urgent).
    /// </summary>
    High = 2,

    /// <summary>
    /// Activités critiques immédiates (ex: appel entrant, micro activé par surprise).
    /// </summary>
    Critical = 3
}
