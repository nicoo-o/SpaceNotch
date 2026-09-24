using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch_App.Views;
using Windows.Foundation;

namespace SpaceNotch_App.Animations;

/// <summary>
/// Moteur de morphing : fait grandir un élément de la forme compacte jusqu'à
/// sa place dans la scène ouverte.
///
/// <para>
/// La pochette de la forme compacte ne disparaît pas pour réapparaître en
/// grand : elle <em>grandit</em>. Le titre ne réapparaît pas : il se déplace.
/// La position de départ est relevée avant que la forme compacte ne soit
/// masquée ; celle d'arrivée, à la première mise en page de la scène. L'élément
/// d'arrivée est alors placé sur celui de départ par une translation et une
/// échelle, puis le compositeur les ramène à l'identité (principe « FLIP ») —
/// aucune mise en page n'est animée.
/// </para>
/// </summary>
internal static class MorphPlayer
{
    /// <summary>Rectangle d'un élément visible dans le repère de <paramref name="root"/>, ou <c>null</c>.</summary>
    public static MorphRect? Measure(FrameworkElement? element, UIElement root)
    {
        if (element is null || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return null;
        }

        try
        {
            Rect bounds = element
                .TransformToVisual(root)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

            return new MorphRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
        catch (Exception)
        {
            // Un élément qui vient de quitter l'arbre n'a plus de position : pas de
            // morphing pour lui, et c'est tout.
            return null;
        }
    }

    /// <summary>
    /// Joue le morphing dès la première mise en page de la scène.
    /// </summary>
    public static void PlayWhenLaidOut(
        IReadOnlyDictionary<MorphAnchorKind, MorphRect> sources,
        IIslandSceneView scene,
        UIElement root,
        bool animate)
    {
        if (!animate || sources.Count == 0)
        {
            return;
        }

        FrameworkElement sceneRoot = scene.Root;
        EventHandler<object>? handler = null;

        handler = (_, _) =>
        {
            sceneRoot.LayoutUpdated -= handler;
            Play(sources, scene, root);
        };

        sceneRoot.LayoutUpdated += handler;
    }

    private static void Play(
        IReadOnlyDictionary<MorphAnchorKind, MorphRect> sources,
        IIslandSceneView scene,
        UIElement root)
    {
        // La pochette grandit depuis la pochette compacte — ou, à défaut, depuis
        // le glyphe qui en tenait la place.
        if (sources.TryGetValue(MorphAnchorKind.Artwork, out MorphRect artwork)
            || sources.TryGetValue(MorphAnchorKind.Icon, out artwork))
        {
            Morph(scene.AnchorFor(MorphAnchorKind.Artwork) ?? scene.AnchorFor(MorphAnchorKind.Icon), artwork, root, uniform: false);
        }

        if (sources.TryGetValue(MorphAnchorKind.Title, out MorphRect title))
        {
            Morph(scene.AnchorFor(MorphAnchorKind.Title), title, root, uniform: true);
        }
    }

    private static void Morph(FrameworkElement? target, MorphRect from, UIElement root, bool uniform)
    {
        if (Measure(target, root) is not { } to || target is null)
        {
            return;
        }

        MorphTransform transform = MorphTransform.Between(from, to, uniform);

        if (transform.IsIdentity)
        {
            return;
        }

        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(target);
            Compositor compositor = visual.Compositor;

            ElementCompositionPreview.SetIsTranslationEnabled(target, true);
            visual.CenterPoint = Vector3.Zero;

            CompositionEasingFunction easeOut = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.2f, 0f),
                new Vector2(0f, 1f));

            TimeSpan duration = TimeSpan.FromMilliseconds(MotionPresets.DurationMs(MotionKind.Morph));

            Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0f, new Vector3((float)transform.TranslateX, (float)transform.TranslateY, 0));
            slide.InsertKeyFrame(1f, Vector3.Zero, easeOut);
            slide.Duration = duration;

            Vector3KeyFrameAnimation grow = compositor.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(0f, new Vector3((float)transform.ScaleX, (float)transform.ScaleY, 1));
            grow.InsertKeyFrame(1f, Vector3.One, easeOut);
            grow.Duration = duration;

            visual.StartAnimation("Translation", slide);
            visual.StartAnimation("Scale", grow);
        }
        catch (Exception)
        {
            // Morphing refusé : l'élément est déjà à sa place, rien n'est perdu.
        }
    }
}
