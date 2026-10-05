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

    /// <summary>
    /// Sortie d'une scène au repli : 180 ms, pour un retrait de la forme qui en
    /// dure ~230. À 120 ms, la forme finissait de se refermer vide pendant
    /// ~110 ms (rafale à 16 ms du 2026-10-04). Le repos qui la remplace attend
    /// cette durée.
    /// </summary>
    public static readonly TimeSpan LeaveDuration = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// Fait sortir un contenu en fondu, sans dépassement ; <paramref name="completed"/>
    /// est appelé à la fin du fondu. Renvoie faux quand rien n'est joué (animations
    /// réduites, compositeur qui refuse) : l'appelant replie alors le contenu tout de suite.
    /// </summary>
    public static bool Leave(UIElement element, bool animate, Action completed)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(completed);

        if (!animate)
        {
            return false;
        }

        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            Compositor compositor = visual.Compositor;

            // Accélération (0.4, 0, 1, 1) : le contenu reste lisible au début du
            // retrait, puis s'efface avant que le repos n'arrive.
            CompositionEasingFunction easeIn = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.4f, 0f),
                new Vector2(1f, 1f));

            ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 0f, easeIn);
            fade.Duration = LeaveDuration;

            CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Opacity", fade);
            batch.End();
            batch.Completed += SpaceNotch_App.Diagnostics.Guard.Batch((_, _) => completed());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Rend son opacité à un contenu sorti en fondu : sans cela, rouvert sous
    /// animations réduites (où rien n'est joué), il resterait invisible.
    /// </summary>
    public static void Settle(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Opacity");
            visual.Opacity = 1f;
        }
        catch (Exception)
        {
            // Un visuel déjà détaché n'a plus rien à rétablir.
        }
    }

    /// <summary>Durée d'un chiffre qui roule : court, pour suivre une mesure qui change souvent.</summary>
    public static readonly TimeSpan RollDuration = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// Une mesure qui change — « 3 » devient « 4 », « 61 % » devient « 62 % » —
    /// roule vers le haut : la nouvelle valeur monte de sa hauteur de ligne et se
    /// pose, avec un léger ressort d'échelle. Plus lisible qu'un fondu : l'œil
    /// voit que la valeur a <em>changé</em>, sans relire le mot.
    /// </summary>
    public static void Roll(UIElement element, bool animate, bool upward = true)
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

            float from = upward ? 8f : -8f;

            Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0f, new Vector3(0, from, 0));
            slide.InsertKeyFrame(1f, Vector3.Zero, easeOut);
            slide.Duration = RollDuration;

            ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0.2f);
            fade.InsertKeyFrame(1f, 1f, easeOut);
            fade.Duration = RollDuration;

            visual.StartAnimation("Translation", slide);
            visual.StartAnimation("Opacity", fade);
        }
        catch (Exception)
        {
            // Même repli que l'arrivée : la valeur est déjà écrite.
        }
    }
}
