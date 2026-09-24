namespace SpaceNotch.Core.State;

/// <summary>
/// Représente les états fondamentaux de l'Island.
/// </summary>
public enum IslandState
{
    /// <summary>
    /// Au repos : taille minimale (pill), discrète, attachée au sommet de l'écran.
    /// </summary>
    Closed,

    /// <summary>
    /// Survolée : légère expansion d'anticipation invitant à l'interaction.
    /// </summary>
    Preview,

    /// <summary>
    /// En cours de transition vers l'état étendu (spring animation).
    /// </summary>
    Expanding,

    /// <summary>
    /// Complètement ouverte : contenu riche, contrôles, surface interactive avec fondu atmosphérique.
    /// </summary>
    Expanded,

    /// <summary>
    /// En cours de fermeture vers l'état Closed.
    /// </summary>
    Collapsing
}
