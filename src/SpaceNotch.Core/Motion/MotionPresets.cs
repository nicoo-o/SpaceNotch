using SpaceNotch.Core.Animation;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Préréglage de mouvement présenté à l'utilisateur.
///
/// Les trois nombres du ressort — raideur, amortissement, masse — ne sont pas un
/// vocabulaire d'utilisateur. Trois caractères le sont : on choisit une
/// personnalité de mouvement, et le réglage fin reste accessible pour qui le
/// cherche.
/// </summary>
public enum MotionStyle
{
    /// <summary>Calme : aucun dépassement, réaction posée. Pour qui veut une notch qui se fait oublier.</summary>
    Quiet = 0,

    /// <summary>Naturel : la référence du langage de mouvement.</summary>
    Natural = 1,

    /// <summary>Dynamique : réaction vive, rebond franc.</summary>
    Dynamic = 2,

    /// <summary>Réglé à la main : la vitesse et le rebond ne suivent plus un préréglage.</summary>
    Custom = 3
}

/// <summary>
/// Catégorie d'une transition, qui décide de sa loi de mouvement.
///
/// Toutes les transitions ne se valent pas : un compteur de volume qui change
/// d'une unité n'a pas la même dignité qu'une ouverture. Nommer la catégorie au
/// lieu de choisir des durées au cas par cas est ce qui garde le langage
/// cohérent d'une fonctionnalité à l'autre.
/// </summary>
public enum MotionKind
{
    /// <summary>Retour immédiat : pression, changement de valeur.</summary>
    Quick = 0,

    /// <summary>Transition de contenu ordinaire : un texte remplacé, une icône qui change.</summary>
    Standard = 1,

    /// <summary>Transition lente : l'atmosphère, la teinte, ce qui doit se remarquer à peine.</summary>
    Slow = 2,

    /// <summary>La forme elle-même : ouverture, fermeture, survol. Toujours un ressort.</summary>
    Spring = 3,

    /// <summary>Un élément qui se déplace d'une présentation à l'autre, sans disparaître.</summary>
    Morph = 4
}

/// <summary>
/// Les lois de mouvement de SpaceNotch, rassemblées en un seul endroit.
///
/// Règle d'usage : une animation doit répondre à « pourquoi ça bouge ? ». Si la
/// réponse est « parce que c'est joli », elle n'a rien à faire ici.
/// </summary>
public static class MotionPresets
{
    /// <summary>Ressort de la forme pour un préréglage.</summary>
    public static SpringParameters Spring(MotionStyle style) => style switch
    {
        MotionStyle.Quiet => SpringParameters.FromResponse(0.52, 0.92),
        MotionStyle.Dynamic => SpringParameters.FromResponse(0.38, 0.48),

        // Naturel reprend le ressort de référence : c'est lui que tout le reste
        // du projet a été réglé à regarder.
        _ => SpringParameters.Default
    };

    /// <summary>
    /// Durée d'une transition temporelle, en millisecondes.
    ///
    /// Les transitions de contenu sont volontairement plus courtes que le
    /// ressort de la forme : le contenu doit avoir fini d'apparaître avant que
    /// la forme ait fini de bouger, sinon on lit un contenu qui court après son
    /// contenant.
    /// </summary>
    public static double DurationMs(MotionKind kind) => kind switch
    {
        MotionKind.Quick => 120,
        MotionKind.Standard => 220,
        MotionKind.Slow => 420,
        MotionKind.Morph => 280,
        _ => 460
    };

    /// <summary>
    /// Durée sous réduction des animations : tout devient un fondu court, et la
    /// forme se pose sans ressort. Ce n'est pas un repli dégradé mais le
    /// comportement demandé par l'utilisateur.
    /// </summary>
    public static double ReducedDurationMs(MotionKind kind) => kind == MotionKind.Slow ? 160 : 90;
}
