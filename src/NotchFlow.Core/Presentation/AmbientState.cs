using System;
using NotchFlow.Core.Activities;
using NotchFlow.Core.Motion;

namespace NotchFlow.Core.Presentation;

/// <summary>
/// L'atmosphère sous la notch, décrite comme une donnée.
///
/// <para>
/// L'activité influence l'ambiance sans connaître la fenêtre décorative : elle
/// déclare une teinte et un état de travail, et cette structure en déduit
/// l'intensité. La couche de rendu ne fait que projeter — c'est la même règle
/// que pour la géométrie.
/// </para>
///
/// <para>
/// L'accent est une <em>information</em>, pas une décoration : les intensités
/// restent faibles, et la teinte est un halo, jamais un dégradé saturé.
/// </para>
/// </summary>
/// <param name="Tint">Teinte du halo et de la dissolution.</param>
/// <param name="Intensity">Présence du halo, de 0 à 1.</param>
/// <param name="TintOpacity">Opacité de la teinte dans le halo, de 0 à 1.</param>
/// <param name="Pulse">
/// Amplitude de la respiration imposée par le mouvement hypnotique, de 0 (aucune)
/// à 1. Elle module l'atmosphère sans en changer la base.
/// </param>
public readonly record struct AmbientState(
    ActivityTint Tint,
    double Intensity,
    double TintOpacity,
    double Pulse)
{
    /// <summary>Atmosphère sans activité : une présence neutre, à peine perceptible.</summary>
    public static AmbientState Neutral => new(ActivityTint.Default, 0.18, 0.40, 0);

    /// <summary>
    /// Atmosphère d'une activité.
    /// </summary>
    /// <param name="activity">Activité présentée, ou <c>null</c>.</param>
    /// <param name="stateTint">
    /// Teinte de l'état, utilisée quand l'activité n'en déclare aucune : un halo
    /// neutre à côté d'un glyphe ambré se lirait comme une lumière d'une autre
    /// source.
    /// </param>
    /// <param name="highContrast">En contraste élevé, l'atmosphère reste neutre.</param>
    public static AmbientState For(IslandActivity? activity, ActivityTint stateTint, bool highContrast)
    {
        if (activity is null)
        {
            return Neutral;
        }

        bool declared = activity.Tint is not null;
        ActivityTint tint = highContrast ? ActivityTint.Default : activity.Tint ?? stateTint;

        double intensity = declared ? 0.34 : 0.18;
        double tintOpacity = declared ? 0.75 : 0.40;

        // Un travail en cours rend l'atmosphère un peu plus présente : c'est ce
        // qui fait lire la notch comme une matière qui travaille, et pas
        // seulement comme un voyant.
        HypnoticPreset preset = HypnoticField.Resolve(activity.MotionState, activity.MotionPreset);
        double pulse = 0;

        if (preset != HypnoticPreset.None && !highContrast)
        {
            intensity = Math.Min(1, intensity + 0.10);
            pulse = HypnoticField.IsLooping(preset) ? 0.30 : 0.45;
        }

        return new AmbientState(tint, intensity, tintOpacity, pulse);
    }
}
