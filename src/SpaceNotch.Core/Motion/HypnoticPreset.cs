namespace SpaceNotch.Core.Motion;

/// <summary>
/// Où en est le travail d'une activité — indépendamment de sa forme.
///
/// <para>
/// C'est la seconde dimension de l'état de SpaceNotch. La première, la
/// présentation (Hidden, Compact, Preview, Expanded), dit <em>combien</em> la
/// notch montre ; celle-ci dit <em>ce que fait</em> ce qu'elle montre. Les deux
/// se combinent librement : « Compact + Working » donne une petite notch
/// habitée d'une matière vivante, « Expanded + Working » la même matière à
/// côté d'une barre de progression.
/// </para>
/// </summary>
public enum ActivityMotionState
{
    /// <summary>Rien ne travaille. Aucun mouvement — c'est l'état de la plupart des activités.</summary>
    Idle = 0,

    /// <summary>Quelque chose commence ou attend : la matière respire, lentement.</summary>
    Attention = 1,

    /// <summary>Un travail est en cours : lecture, recherche, transfert, synchronisation.</summary>
    Working = 2,

    /// <summary>Le travail se termine : la matière converge, pulse, puis se tait.</summary>
    Completing = 3,

    /// <summary>Terminé. Plus aucun mouvement ; le résultat se lit.</summary>
    Complete = 4,

    /// <summary>Échec : interruption, dispersion, micro-secousse.</summary>
    Error = 5
}

/// <summary>
/// Préréglages du mouvement hypnotique.
///
/// <para>
/// Inspirés de la notch HUD « Hypnotizing UI » d'Inspora — qui anime une IA
/// pendant qu'elle lit, réfléchit et construit — mais <b>détachés de l'IA</b> :
/// ici, chaque préréglage signifie une nature de travail. Un greffon ne crée
/// jamais sa propre animation, il demande un préréglage et SpaceNotch s'occupe
/// du reste. C'est ce qui donne un langage de mouvement commun à tout
/// l'écosystème. Voir ADR-018.
/// </para>
///
/// <para>
/// <b>Règle d'usage.</b> Le mouvement hypnotique signifie « quelque chose est en
/// train de travailler, chercher, absorber ou se transformer ». Jamais « quelque
/// chose vient de se produire » : le volume, la luminosité, lecture/pause n'en
/// portent pas.
/// </para>
/// </summary>
public enum HypnoticPreset
{
    /// <summary>Aucun mouvement.</summary>
    None = 0,

    /// <summary>Lecture : lent, régulier, respirant.</summary>
    Read = 1,

    /// <summary>Réflexion : organique, moins prévisible.</summary>
    Think = 2,

    /// <summary>Recherche : directionnel, balayage puis convergence.</summary>
    Search = 3,

    /// <summary>Traitement : dense, énergique — téléchargement, installation, indexation.</summary>
    Process = 4,

    /// <summary>Synchronisation : flux gauche ↔ droite.</summary>
    Sync = 5,

    /// <summary>Dépôt : attraction vers le centre — la notch absorbe.</summary>
    Drop = 6,

    /// <summary>Achèvement, une seule fois : convergence, impulsion, silence.</summary>
    Complete = 7,

    /// <summary>Échec, une seule fois : interruption, dispersion, micro-secousse.</summary>
    Error = 8
}
