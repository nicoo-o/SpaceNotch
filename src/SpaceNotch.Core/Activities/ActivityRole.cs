namespace SpaceNotch.Core.Activities;

/// <summary>
/// Rôle d'une activité que l'utilisateur ne doit jamais perdre de vue, même
/// quand une autre occupe la notch. Voir <see cref="Presentation.SplitPresentation"/>.
/// </summary>
public enum ActivityRole
{
    /// <summary>Aucun rôle particulier.</summary>
    None = 0,

    /// <summary>Un fichier est en cours de téléchargement.</summary>
    Download = 1,

    /// <summary>Un appel est en cours ou entrant.</summary>
    Call = 2,

    /// <summary>Le micro, la caméra ou l'écran sont enregistrés.</summary>
    Recording = 3
}
