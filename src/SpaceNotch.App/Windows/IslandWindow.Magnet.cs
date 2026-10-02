using System;
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
            timer.Interval = MagnetFar;
            return;
        }

        DisplayInfo display = DetachDisplay();
        (double x, double y) = _tourCursor ?? CursorDip();
        IslandFootprint rest = _restFootprint;
        var notch = new ScreenRect(AttachCenterX(display) - (rest.Width / 2), 0, rest.Width, rest.Height);

        MagnetPull pull = Magnet.For(x, y, notch);
        _controller.Lean(pull.Scale);

        // Loin : un regard toutes les 250 ms suffit ; proche : 40 ms, pour que
        // l'attraction suive la main sans à-coup.
        double gap = Math.Max(0, y - notch.Bottom);
        timer.Interval = gap < Magnet.Reach * 2 ? MagnetNear : MagnetFar;
    }
}
