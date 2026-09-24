using System;
using SpaceNotch.Core.Presentation;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Apaisement du mouvement hypnotique lorsqu'il dure.
///
/// <para>
/// Un mouvement automatique qui dure plus de cinq secondes, affiché à côté
/// d'autres contenus, doit pouvoir être arrêté (WCAG 2.2.2). Le réglage
/// « Mouvement hypnotique » le permet ; ceci va plus loin : dans la forme
/// compacte, une boucle qui tourne depuis longtemps n'apprend plus rien — elle
/// a dit « ça travaille » dès sa première seconde. Passé un délai, elle se fige
/// sur un motif fixe. Elle reprend dès que l'utilisateur regarde (aperçu,
/// ouverture) ou que le travail change d'état.
/// </para>
/// </summary>
public static class HypnoticAttenuation
{
    /// <summary>Durée après laquelle une boucle compacte se fige.</summary>
    public static TimeSpan Delay { get; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Vrai si le mouvement doit être figé.
    /// </summary>
    /// <param name="preset">Préréglage en cours.</param>
    /// <param name="running">Depuis combien de temps il tourne sans interruption.</param>
    /// <param name="presentation">Ce que la notch montre.</param>
    public static bool ShouldRest(HypnoticPreset preset, TimeSpan running, NotchPresentation presentation)
    {
        // Les passages uniques — achèvement, échec — sont courts par nature ; et
        // quand l'utilisateur regarde, le mouvement est l'information qu'il vient
        // chercher.
        if (!HypnoticField.IsLooping(preset)
            || presentation is NotchPresentation.Preview or NotchPresentation.Expanded)
        {
            return false;
        }

        return running >= Delay;
    }
}
