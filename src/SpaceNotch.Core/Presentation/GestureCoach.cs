using System;
using System.Collections.Generic;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Scenes;

namespace SpaceNotch.Core.Presentation;

/// <summary>Un geste à enseigner maintenant : sa clé (mémorisée une fois appris) et ce que la rangée d'aide affiche.</summary>
public readonly record struct GestureLesson(string Key, GestureTip Tip);

/// <summary>
/// Gestes enseignés en contexte (ADR-028, volet A). L'aide des gestes existait
/// (<see cref="GestureHelp"/>) mais ne se montrait qu'au survol avec Alt tenu,
/// un geste que personne ne devine. Elle se montre désormais d'elle-même, une
/// fois par session, quand un geste devient utile — et plus jamais une fois
/// ce geste utilisé.
/// </summary>
public static class GestureCoach
{
    /// <summary>La molette règle le volume d'une musique présentée.</summary>
    public const string WheelVolume = "wheel.volume";

    /// <summary>Ctrl + molette parcourt la pile du presse-papier.</summary>
    public const string WheelStack = "wheel.stack";

    /// <summary>Durée d'affichage : le temps de lire trois ou quatre mots.</summary>
    public static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Le geste à enseigner maintenant, ou <c>null</c>. Seulement notch au repos
    /// (ouverte, l'utilisateur est déjà occupé), et un geste à la fois.
    /// </summary>
    /// <param name="presented">Activité présentée, ou <c>null</c> au repos vide.</param>
    /// <param name="atRest">Vrai si la notch est refermée sur sa forme de repos.</param>
    /// <param name="learned">Gestes déjà utilisés (mémorisés dans les réglages).</param>
    /// <param name="shown">Gestes déjà enseignés pendant cette session.</param>
    public static GestureLesson? Next(
        IslandActivity? presented,
        bool atRest,
        IReadOnlyCollection<string> learned,
        IReadOnlyCollection<string> shown)
    {
        ArgumentNullException.ThrowIfNull(learned);
        ArgumentNullException.ThrowIfNull(shown);

        if (!atRest || presented is null)
        {
            return null;
        }

        // La fonctionnalité et la scène : la musique de la visite ou de la démo a
        // la scène Media, mais la molette n'y règle pas le volume
        // (NotchGestures.WheelControlsVolume regarde aussi la fonctionnalité) ;
        // la couleur copiée vient du presse-papier, mais son aide n'est pas la
        // pile, et la leçon montrée doit être celle de sa clé.
        string? key = (presented.FeatureId, presented.SceneKey) switch
        {
            (FeatureKeys.Media, IslandSceneCatalog.Media) => WheelVolume,
            (FeatureKeys.Clipboard, IslandSceneCatalog.Clipboard) => WheelStack,
            _ => null
        };

        if (key is null || Contains(learned, key) || Contains(shown, key))
        {
            return null;
        }

        // Le premier geste de l'aide est le plus utile : c'est lui qu'on enseigne.
        IReadOnlyList<GestureTip> tips = GestureHelp.For(presented);
        return tips.Count == 0 ? null : new GestureLesson(key, tips[0]);
    }

    private static bool Contains(IReadOnlyCollection<string> keys, string key)
    {
        foreach (string k in keys)
        {
            if (string.Equals(k, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
