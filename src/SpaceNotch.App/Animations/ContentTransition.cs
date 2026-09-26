using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace SpaceNotch_App.Animations;

/// <summary>
/// Transition d'un contenu remplacé sur place : un texte qui change, une icône
/// qui passe à la suivante.
///
/// <para>
/// Dans la référence, « Reading file » ne disparaît pas pour laisser la place à
/// « Thinking » : le nouveau texte apparaît <em>à la même place</em>, par un
/// fondu court. C'est la plus petite unité du moteur de morphing — l'élément
/// reste, seul son contenu change.
/// </para>
///
/// <para>
/// C'est une transition d'<em>effet</em> : elle ne dépasse jamais sa cible. Seule
/// la géométrie rebondit ; l'opacité et le déplacement de quelques DIPs suivent
/// une courbe de décélération, et finissent avant la forme.
/// </para>
/// </summary>
internal static class ContentTransition
{
    /// <summary>Opacité de départ : le voile (flou 6 → 0) porte la matière, le fondu part de rien.</summary>
    private const float StartOpacity = 0f;

    /// <summary>Montée verticale, en DIPs : juste assez pour qu'on lise un arrivant.</summary>
    private const float Rise = 6f;

    /// <summary>Durée d'arrivée validée pour la vague 2 : 220 ms, décélération.</summary>
    public static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(220);

    /// <summary>Changement d'activité : le nouveau contenu arrive en 200 ms.</summary>
    public static readonly TimeSpan SwapDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Joue la transition d'arrivée sur un élément dont le contenu vient de
    /// changer. Rien n'est joué sous réduction des animations.
    /// </summary>
    /// <param name="element">Élément dont le contenu vient de changer.</param>
    /// <param name="animate">Faux sous réduction des animations : rien n'est joué.</param>
    /// <param name="delay">
    /// Attente avant l'arrivée. À l'ouverture, le contenu attend que la forme ait
    /// pris l'essentiel de sa place : forme et contenu suivent une seule ligne de
    /// temps, au lieu d'arriver ensemble et de se bousculer.
    /// </param>
    public static void Play(UIElement element, bool animate, TimeSpan delay = default, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (!animate)
        {
            return;
        }

        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            Compositor compositor = visual.Compositor;

            ElementCompositionPreview.SetIsTranslationEnabled(element, true);

            // Décélération (0, 0, 0, 1) : l'arrivant freine jusqu'à sa place.
            CompositionEasingFunction easeOut = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0f, 0f),
                new Vector2(0f, 1f));

            TimeSpan length = duration ?? EnterDuration;

            ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, StartOpacity);
            fade.InsertKeyFrame(1f, 1f, easeOut);
            fade.Duration = length;
            fade.DelayTime = delay;
            fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0f, new Vector3(0, Rise, 0));
            slide.InsertKeyFrame(1f, Vector3.Zero, easeOut);
            slide.Duration = length;
            slide.DelayTime = delay;
            slide.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            visual.StartAnimation("Opacity", fade);
            visual.StartAnimation("Translation", slide);
        }
        catch (Exception)
        {
            // Une transition refusée par le compositeur laisse le contenu à sa
            // place : c'est le repli correct, pas une panne.
        }
    }
}
