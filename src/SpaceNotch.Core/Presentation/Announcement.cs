using System;
using SpaceNotch.Core.Activities;

namespace SpaceNotch.Core.Presentation;

/// <summary>
/// Ce qu'un lecteur d'écran doit entendre quand une activité arrive.
/// </summary>
/// <param name="Text">Phrase à annoncer.</param>
/// <param name="Assertive">
/// Vrai pour interrompre la lecture en cours — un appel entrant ; faux pour
/// attendre la fin de la phrase courante.
/// </param>
public readonly record struct Announcement(string Text, bool Assertive)
{
    /// <summary>
    /// Annonce d'une activité, ou <c>null</c> si elle n'a rien à dire : une
    /// republication, un retour système.
    ///
    /// <para>
    /// La notch est visuelle par nature ; sans annonce, quiconque utilise
    /// Narrateur ne saurait jamais qu'un téléchargement a fini ou qu'un appel
    /// arrive. Le niveau d'insistance suit la politique de cohabitation : seule
    /// une activité qui interrompt interrompt aussi la lecture.
    /// </para>
    /// </summary>
    public static Announcement? For(IslandActivity? activity, bool isNew)
    {
        if (activity is null || !isNew)
        {
            return null;
        }

        // Un volume ou une luminosité se lisent sur le système lui-même : les
        // annoncer doublerait ce que Windows dit déjà.
        if (activity.State == State.IslandActivityState.SystemHud)
        {
            return null;
        }

        string context = activity.Eyebrow ?? activity.Subtitle ?? activity.Source ?? string.Empty;
        string text = context.Length == 0 ? activity.Title : $"{activity.Title}, {context}";

        bool assertive = ActivityPolicies.Resolve(activity) == ActivityPresentationPolicy.Interrupting;

        return new Announcement(text, assertive);
    }
}
