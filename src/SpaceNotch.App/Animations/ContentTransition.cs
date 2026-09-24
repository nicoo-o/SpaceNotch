using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using SpaceNotch.Core.Motion;

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
    /// <summary>Opacité de départ : le texte ne part pas du vide, il se précise.</summary>
    private const float StartOpacity = 0.15f;

    /// <summary>Montée verticale, en DIPs : juste assez pour qu'on lise un arrivant.</summary>
    private const float Rise = 4f;

    /// <summary>
    /// Joue la transition d'arrivée sur un élément dont le contenu vient de
    /// changer. Rien n'est joué sous réduction des animations.
    /// </summary>
    public static void Play(UIElement element, bool animate)
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

            CompositionEasingFunction easeOut = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.2f, 0f),
                new Vector2(0f, 1f));

            TimeSpan duration = TimeSpan.FromMilliseconds(MotionPresets.DurationMs(MotionKind.Standard));

            ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, StartOpacity);
            fade.InsertKeyFrame(1f, 1f, easeOut);
            fade.Duration = duration;

            Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0f, new Vector3(0, Rise, 0));
            slide.InsertKeyFrame(1f, Vector3.Zero, easeOut);
            slide.Duration = duration;

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
