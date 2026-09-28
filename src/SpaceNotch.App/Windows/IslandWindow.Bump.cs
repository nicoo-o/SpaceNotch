using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Butée (A6) : arrivé au bout — le volume à 100 % qu'on pousse encore, ou à
/// zéro qu'on baisse encore — le contenu fait une micro-secousse de 2 DIP. La
/// limite se sent comme un bord physique au lieu d'un refus muet.
/// </summary>
public sealed partial class IslandWindow
{
    private long _lastBump;

    private void BumpContent()
    {
        long now = Environment.TickCount64;

        // Une molette qui insiste ne secoue pas en continu : une butée par geste.
        if (!UseSpringAnimations() || now - _lastBump < 350)
        {
            return;
        }

        _lastBump = now;

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(ContentArea, true);
            Visual visual = ElementCompositionPreview.GetElementVisual(ContentArea);
            Compositor compositor = visual.Compositor;
            Vector3KeyFrameAnimation shake = compositor.CreateVector3KeyFrameAnimation();
            int steps = MotionPresets.Bump.Count;

            shake.InsertKeyFrame(0f, Vector3.Zero);

            for (int i = 0; i < steps; i++)
            {
                shake.InsertKeyFrame((i + 1f) / steps, new Vector3((float)MotionPresets.Bump[i], 0, 0));
            }

            shake.Duration = TimeSpan.FromMilliseconds(30 * steps);
            visual.StartAnimation("Translation", shake);
        }
        catch (Exception)
        {
            // Sans compositeur, pas de secousse : la valeur reste simplement au bout.
        }
    }
}
