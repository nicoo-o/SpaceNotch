using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Dispatching;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Display;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Notch magnétique (U4) : à moins de 120 DIPs, la notch au repos grossit à
/// peine vers le curseur — on la sent vivante et cliquable avant même de la
/// survoler. La position du curseur n'est lue qu'à un rythme lent tant qu'il
/// est loin, et plus vite quand il approche ; rien quand la notch est ouverte,
/// détachée ou sur un côté, ni quand Windows réduit les animations.
/// </summary>
public sealed partial class IslandWindow
{
    private static readonly TimeSpan MagnetFar = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MagnetNear = TimeSpan.FromMilliseconds(40);

    private DispatcherQueueTimer? _magnetTimer;

    /// <summary>Curseur imaginaire de la visite filmée, en DIPs d'écran ; <c>null</c> : le vrai.</summary>
    private (double X, double Y)? _tourCursor;

    private void StartMagnet()
    {
        _magnetTimer ??= CreateMagnetTimer();
        _magnetTimer.Start();
    }

    /// <summary>La notch retirée (plein écran) : l'aimant n'a rien à attirer.</summary>
    private void StopMagnet()
    {
        _magnetTimer?.Stop();
        _controller.Lean(1);
        LeanToward(0);
    }

    private DispatcherQueueTimer CreateMagnetTimer()
    {
        DispatcherQueueTimer timer = _dispatcherQueue.CreateTimer();
        timer.Interval = MagnetFar;
        timer.IsRepeating = true;
        timer.Tick += (_, _) => StepMagnet(timer);
        return TrackTimer(timer);
    }

    private void StepMagnet(DispatcherQueueTimer timer)
    {
        if (_isClosed)
        {
            timer.Stop();
            return;
        }

        if (!_islandShown || UsesFloatingGeometry || UsesSideTab || _pointerDown
            || _controller.State != IslandState.Closed || !UseSpringAnimations())
        {
            _controller.Lean(1);
            LeanToward(0);
            timer.Interval = MagnetFar;
            return;
        }

        // L'écran est lu sans être mémorisé : DetachDisplay() le figeait pour de
        // bon (jusqu'au prochain glisser), et la notch ne suivait plus ni le mode
        // « écran du curseur » ni un changement d'écran.
        DisplayInfo display = _detachDisplay ?? ResolveDisplay();
        (double x, double y) = _tourCursor ?? CursorDipOn(display);
        IslandFootprint rest = _restFootprint;
        var notch = new ScreenRect(AttachCenterX(display) - (rest.Width / 2), 0, rest.Width, rest.Height);

        MagnetPull pull = Magnet.For(x, y, notch);
        _controller.Lean(pull.Scale);
        LeanToward(pull.DX);

        // Loin : un regard toutes les 250 ms suffit ; proche : 40 ms, pour que
        // l'attraction suive la main sans à-coup.
        // « Proche » se mesure dans les deux sens : un pointeur en haut de l'écran
        // mais loin sur le côté (des onglets, une barre de titre) n'a rien à voir
        // avec la notch, et ne doit pas faire passer l'aimant à 25 Hz.
        double gap = Math.Max(0, y - notch.Bottom);
        double side = Math.Max(0, Math.Abs(x - (notch.X + (notch.Width / 2))) - (notch.Width / 2));
        timer.Interval = gap < Magnet.Reach * 2 && side < Magnet.Reach * 2 ? MagnetNear : MagnetFar;
    }

    private double _leanX;

    /// <summary>
    /// La notch se penche vers le curseur, de côté seulement : collée au haut
    /// de l'écran, elle ne s'en détache jamais. Le décalage glisse en 120 ms.
    /// </summary>
    private void LeanToward(double dx)
    {
        dx = Math.Round(dx * 2) / 2;

        if (Math.Abs(dx - _leanX) < 0.25)
        {
            return;
        }

        _leanX = dx;

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(IslandBody, true);
            Visual visual = ElementCompositionPreview.GetElementVisual(IslandBody);
            Vector3KeyFrameAnimation slide = visual.Compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(1f, new Vector3((float)dx, 0, 0));
            slide.Duration = TimeSpan.FromMilliseconds(120);
            visual.StartAnimation("Translation", slide);
        }
        catch (Exception)
        {
            // Sans compositeur, la notch grossit seulement.
        }
    }
}
