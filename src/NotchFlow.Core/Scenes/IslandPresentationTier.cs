using NotchFlow.Core.Activities;

namespace NotchFlow.Core.Scenes;

/// <summary>
/// Forme que prend l'Island <em>fermée</em> pour présenter une activité.
///
/// C'est une dimension à part entière, et non un cas particulier de la scène :
/// la scène décrit le contenu <em>déployé</em>, le palier décrit ce que l'Island
/// montre d'elle-même avant qu'on l'ouvre. Une lecture musicale et une
/// notification ont des scènes très différentes, mais un palier qui ne dépend
/// que de leur urgence relative.
/// </summary>
public enum IslandPresentationTier
{
    /// <summary>
    /// Veille : rien à signaler. Un point neutre, et rien d'autre.
    ///
    /// C'est l'état que l'utilisateur voit le plus longtemps, donc celui qui doit
    /// coûter le moins d'attention.
    /// </summary>
    Idle = 0,

    /// <summary>
    /// Signal : une activité vit en arrière-plan. Son glyphe et son libellé
    /// court, sur une seule ligne.
    ///
    /// Une musique qui joue ne mérite pas de réclamer deux lignes en permanence ;
    /// elle mérite d'être constatable sans être lue.
    /// </summary>
    Signal = 1,

    /// <summary>
    /// Carte : une activité qui mérite d'être lue — deux lignes, glyphe, état.
    /// </summary>
    Card = 2
}

/// <summary>
/// Règles de résolution du palier de présentation.
/// </summary>
public static class IslandPresentation
{
    /// <summary>
    /// Palier d'une activité.
    ///
    /// La priorité suffit dans l'immense majorité des cas, ce qui évite à chaque
    /// fonctionnalité d'avoir à se décrire deux fois — une fois par son urgence,
    /// une fois par sa forme. La surcharge explicite existe pour les cas où les
    /// deux divergent légitimement : un téléchargement de plusieurs heures est
    /// d'arrière-plan mais mérite une carte.
    /// </summary>
    public static IslandPresentationTier Resolve(IslandActivity? activity)
    {
        if (activity is null)
        {
            return IslandPresentationTier.Idle;
        }

        return activity.Presentation ?? FromPriority(activity.Priority);
    }

    /// <summary>
    /// Traduit une urgence en forme. Les activités d'arrière-plan ne dépassent
    /// jamais le palier signal : c'est ce qui garantit qu'un lecteur de musique
    /// laissé ouvert ne transforme pas l'Island en bandeau permanent.
    /// </summary>
    public static IslandPresentationTier FromPriority(ActivityPriority priority)
        => priority == ActivityPriority.Background
            ? IslandPresentationTier.Signal
            : IslandPresentationTier.Card;
}
