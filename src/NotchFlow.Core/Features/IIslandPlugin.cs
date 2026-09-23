using System.Collections.Generic;
using NotchFlow.Core.Activities;
using NotchFlow.Core.Events;

namespace NotchFlow.Core.Features;

/// <summary>
/// Dépendances que l'hôte met à disposition d'une fonctionnalité.
///
/// Le contexte est volontairement étroit : un greffon ne reçoit pas l'application,
/// ni ses fenêtres, ni son état. Il reçoit de quoi publier une activité et de quoi
/// écouter des événements — l'exact nécessaire pour être une fonctionnalité, et
/// rien de plus.
/// </summary>
public sealed record IslandFeatureContext(IActivityManager Activities, IEventBus Events);

/// <summary>
/// Contrat d'un greffon externe.
///
/// Un greffon est une fabrique, pas une fonctionnalité : il crée ses
/// fonctionnalités au chargement, ce qui lui permet de leur injecter ses propres
/// dépendances internes. C'est aussi ce qui rend le chargement vérifiable — un
/// greffon qui ne peut pas produire de fonctionnalité est un greffon invalide, et
/// cela se voit immédiatement.
/// </summary>
public interface IIslandPlugin
{
    /// <summary>Nom lisible, utilisé dans les diagnostics et les réglages.</summary>
    string Name { get; }

    /// <summary>
    /// Crée les fonctionnalités apportées par le greffon.
    ///
    /// Chaque fonctionnalité retournée suit le même cycle de vie que les
    /// fonctionnalités intégrées : elle peut être activée, désactivée, et sa
    /// désactivation doit libérer ce qu'elle a acquis.
    /// </summary>
    IEnumerable<IIslandFeature> CreateFeatures(IslandFeatureContext context);
}
