using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;

namespace SpaceNotch.Core.Presentation;

/// <summary>
/// Ce que la notch montre d'elle-même : la couche de présentation du plan
/// SpaceNotch 2.0.
///
/// <para>
/// <see cref="IslandState"/> décrit la mécanique — une transition est-elle en
/// cours ? — et reste la source de vérité du ressort. La présentation en est la
/// lecture produit : combien la notch se montre. Toutes les présentations
/// partagent la même géométrie, <see cref="IslandGeometryMode.TopAttached"/> :
/// elles changent la taille de la découpe, jamais sa nature.
/// </para>
/// </summary>
public enum NotchPresentation
{
    /// <summary>Aucune activité pertinente : une lèvre au bord de l'écran, presque rien.</summary>
    Hidden = 0,

    /// <summary>La forme normale : savoir qu'il se passe quelque chose.</summary>
    Compact = 1,

    /// <summary>Le pointeur approche : un peu plus d'information, pour donner envie d'interagir.</summary>
    Preview = 2,

    /// <summary>L'utilisateur a demandé à voir : une vraie surface d'interaction.</summary>
    Expanded = 3
}

/// <summary>
/// Résout la présentation à partir de l'état mécanique et de l'activité.
///
/// La règle est volontairement courte : c'est l'intention de l'utilisateur qui
/// ouvre (le clic), la proximité qui prévisualise (le survol), et la seule
/// existence d'une activité qui fait passer de la lèvre à la forme compacte.
/// </summary>
public static class NotchPresentationResolver
{
    public static NotchPresentation Resolve(IslandState state, IslandActivity? activity) => state switch
    {
        IslandState.Expanding or IslandState.Expanded => NotchPresentation.Expanded,
        IslandState.Preview => NotchPresentation.Preview,
        _ => activity is null ? NotchPresentation.Hidden : NotchPresentation.Compact
    };

    /// <summary>
    /// Palier de repos correspondant à une présentation, pour les formes qui ne
    /// dépendent pas d'une scène déployée.
    /// </summary>
    public static IslandPresentationTier RestingTier(IslandActivity? activity)
        => IslandPresentation.Resolve(activity);

    /// <summary>
    /// Palier affiché pendant un aperçu : depuis le signal, l'aperçu porte la
    /// seconde ligne ; sinon il tient la forme acquise.
    /// </summary>
    public static IslandPresentationTier PreviewTier(IslandPresentationTier resting)
        => resting == IslandPresentationTier.Signal ? IslandPresentationTier.Card : resting;
}
