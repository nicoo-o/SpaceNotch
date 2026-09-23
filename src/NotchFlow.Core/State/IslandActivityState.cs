namespace NotchFlow.Core.State;

/// <summary>
/// Nature contextuelle d'une activité, indépendante de la géométrie.
///
/// <see cref="IslandState"/> décrit la <em>forme</em> de l'Island (fermée,
/// ouverte, en transition) ; <see cref="IslandActivityState"/> décrit ce que
/// l'Island raconte. Les deux dimensions sont orthogonales : une Island ouverte
/// peut présenter une notification comme une lecture en cours.
/// </summary>
public enum IslandActivityState
{
    /// <summary>Aucun contenu contextuel : l'Island est une simple présence.</summary>
    Idle = 0,

    /// <summary>Lecture média en cours ou en pause.</summary>
    MediaActive,

    /// <summary>Appel entrant ou en cours (visio, téléphonie).</summary>
    CallActive,

    /// <summary>Téléchargement ou transfert en progression.</summary>
    DownloadActive,

    /// <summary>Un fichier survole l'Island pour y être déposé.</summary>
    FileDrag,

    /// <summary>Notification applicative affichée temporairement.</summary>
    Notification,

    /// <summary>Retour visuel système : volume, luminosité, micro, muet.</summary>
    SystemHud,

    /// <summary>Compte à rebours ou chronomètre actif.</summary>
    TimerActive,

    /// <summary>Périphérique connecté ou déconnecté.</summary>
    DeviceActive
}
