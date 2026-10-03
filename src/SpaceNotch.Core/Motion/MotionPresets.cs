using SpaceNotch.Core.Activities;
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

        // Naturel : physique B « Liquide doux », choisie à l'audit d'octobre
        // 2026 (réponse 0,46 s, amortissement 0,62, soit environ 8 % de
        // dépassement) : la notch se lit comme une matière, sans trembler.
        _ => NaturalOpen
    };

    /// <summary>Ouverture « Naturel » (Liquide doux) : 0,46 s, amortissement 0,62.</summary>
    public static SpringParameters NaturalOpen { get; } = SpringParameters.FromResponse(0.46, 0.62);

    /// <summary>« Naturel » de la vague 2 (0,42 / 0,78), suivi par les configurations restées dessus.</summary>
    public static SpringParameters PreviousNaturalOpen { get; } = SpringParameters.FromResponse(0.42, 0.78);

    /// <summary>Amortissement minimal de la fermeture.</summary>
    public const double CloseDamping = 0.82;

    /// <summary>
    /// Ressort de fermeture, dérivé de celui de l'ouverture : plus court
    /// (0,38 / 0,46) et presque sans rebond (amortissement au moins 0,82, soit
    /// moins de 1 % de dépassement). Une notch qui rebondit franchement en se
    /// refermant a l'air de refuser de partir.
    /// </summary>
    public static SpringParameters CloseOf(SpringParameters open)
        => SpringParameters.FromResponse(
            open.ResponseSeconds * (0.38 / 0.46),
            Math.Max(open.DampingRatio, CloseDamping),
            open.Mass);

    /// <summary>
    /// Ressort selon l'importance (A7) : la physique porte le sens. Une
    /// information qui s'ouvre d'elle-même sans être urgente se pose presque
    /// sans dépasser (amortissement 0,80) ; une interruption (appel, alarme)
    /// s'ouvre avec un rebond franc (0,50).
    /// On devine l'importance avant de lire.
    ///
    /// <para>
    /// Le préréglage « Calme » est respecté : qui a choisi une notch sans rebond
    /// n'en reçoit pas, même pour une alarme.
    /// </para>
    /// </summary>
    public static SpringParameters ForPriority(SpringParameters chosen, ActivityPriority priority)
    {
        ArgumentNullException.ThrowIfNull(chosen);

        if (chosen.DampingRatio >= 0.9)
        {
            return chosen;
        }

        return priority >= ActivityPriority.High
            ? SpringParameters.FromResponse(chosen.ResponseSeconds, Math.Min(chosen.DampingRatio, UrgentDamping), chosen.Mass)
            : SpringParameters.FromResponse(chosen.ResponseSeconds, Math.Max(chosen.DampingRatio, NormalDamping), chosen.Mass);
    }

    /// <summary>Amortissement d'une ouverture urgente (A7).</summary>
    public const double UrgentDamping = 0.50;

    /// <summary>Amortissement d'une ouverture spontanée ordinaire : un soupçon de rebond.</summary>
    public const double NormalDamping = 0.80;

    /// <summary>
    /// Écrasement maximal au rebond, à volume constant : la forme qui dépasse
    /// sa hauteur s'amincit d'au plus 7 %, et s'élargit d'autant en retombant.
    /// </summary>
    public const double MaxSquash = 0.07;

    /// <summary>
    /// Bas de la forme gonflé par la vitesse verticale (Liquide doux), en DIP :
    /// positif quand la forme descend (le bas se bombe), négatif quand elle
    /// remonte (le bas se creuse un peu). 0,35 × clamp(v / 120, −6, 10).
    /// </summary>
    public static double Bulge(double heightVelocity)
        => double.IsFinite(heightVelocity) ? 0.35 * Math.Clamp(heightVelocity / 120, -6, 10) : 0;

    /// <summary>
    /// Butée (A6) : arrivé au bout (volume à 100 %, fin de liste), le contenu
    /// fait une micro-secousse. Déplacements successifs en DIP, un par image de
    /// 30 ms : trois oscillations en 120 ms, amplitude 2 DIP qui décroît.
    /// </summary>
    public static IReadOnlyList<double> Bump { get; } = [2.4, -1.8, 1.0, -0.4, 0];

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
