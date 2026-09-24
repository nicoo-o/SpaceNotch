using System;
using Microsoft.UI.Dispatching;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Display;

namespace SpaceNotch_App.Windows;

/// <summary>
/// La notch qui se partage : une bulle pour l'activité importante qui ne doit
/// pas disparaître derrière celle qui est présentée. Voir
/// <see cref="SplitPresentation"/> et ADR-019.
///
/// <para>
/// Accrochée, la bulle est une mini-notch collée au bord, à côté de la notch.
/// Détachée, la notch l'emmène : la bulle la suit avec un ressort un peu plus
/// mou que le sien, comme une goutte qui traîne. Toucher la bulle échange les
/// rôles — son activité prend la notch, l'autre devient la bulle.
/// </para>
/// </summary>
public sealed partial class IslandWindow
{
    private BubbleWindow _bubble = null!;

    /// <summary>Instant où la bulle a commencé à montrer son activité, pour l'apaisement de sa boucle.</summary>
    private DateTimeOffset _bubbleSince = DateTimeOffset.UtcNow;

    private DispatcherQueueTimer? _bubbleRestTimer;

    /// <summary>Crée la bulle ; appelée une fois, avec la couche décorative.</summary>
    private void CreateBubble()
    {
        _bubble = new BubbleWindow();
        _bubble.Invoked += OnBubbleInvoked;
        _bubble.UseSpringAnimations = UseSpringAnimations();

        // Le pointeur passe de la notch à la bulle : la notch ne doit pas se
        // replier sous lui pendant qu'il vise la bulle.
        _bubble.Hovered += (_, inside) =>
        {
            if (inside)
            {
                _previewExitTimer?.Stop();
            }
        };

        _activityManager.ActivityPosted += (_, _) => OnUiThread(UpdateBubble);
        _activityManager.ActivityRemoved += (_, _) => OnUiThread(UpdateBubble);
    }

    /// <summary>
    /// Décide si la bulle existe, et ce qu'elle montre. Appelée à chaque rendu
    /// et à chaque publication : la règle vit dans le cœur, la fenêtre ne fait
    /// que l'appliquer.
    /// </summary>
    private void UpdateBubble()
    {
        if (_isClosed)
        {
            return;
        }

        IslandActivity? target = _settings.ShowSplitBubble
            ? SplitPresentation.BubbleFor(_controller.PresentedActivity, _activityManager.GetActiveActivities())
            : null;

        // La bulle accompagne la forme compacte. Ouverte, la notch montre
        // l'activité en grand et la bulle se retire, comme sur l'iPhone ; elle
        // revient à la fermeture.
        bool compact = _controller.State is IslandState.Closed or IslandState.Preview;
        bool visible = target is not null && compact && _islandShown;

        if (!visible)
        {
            _bubbleRestTimer?.Stop();
            _bubble.HideBubble();
            return;
        }

        if (!string.Equals(_bubble.ActivityId, target!.Id, StringComparison.Ordinal))
        {
            _bubbleSince = DateTimeOffset.UtcNow;
        }

        HypnoticPreset preset = HypnoticField.Resolve(target.MotionState, target.MotionPreset);
        bool resting = HypnoticAttenuation.ShouldRest(preset, DateTimeOffset.UtcNow - _bubbleSince, NotchPresentation.Compact);

        bool firstShow = !_bubble.IsShown;
        _bubble.UseSpringAnimations = UseSpringAnimations();
        _bubble.Show(target, preset, AnimateHypnotic() && !resting);

        if (firstShow)
        {
            // Le ressort de suivi part de la notch : la bulle en sort.
            ScreenRect origin = UsesFloatingGeometry ? CurrentPillRect() : default;
            _bubbleSpring.Snap(origin.Right, origin.CenterY);
            PositionBubble();
        }

        ArmBubbleRest(preset, resting);
    }

    /// <summary>
    /// Une boucle dans la bulle se fige, comme celle de la notch, après le délai
    /// d'apaisement : un seul minuteur, armé seulement si une boucle tourne.
    /// </summary>
    private void ArmBubbleRest(HypnoticPreset preset, bool resting)
    {
        _bubbleRestTimer?.Stop();

        if (resting || !HypnoticField.IsLooping(preset))
        {
            return;
        }

        TimeSpan remaining = HypnoticAttenuation.Delay - (DateTimeOffset.UtcNow - _bubbleSince);

        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        _bubbleRestTimer ??= CreateOneShotTimer(remaining, UpdateBubble);
        _bubbleRestTimer.Interval = remaining;
        _bubbleRestTimer.Start();
    }

    private void OnBubbleInvoked(object? sender, string activityId)
    {
        // Un geste de l'utilisateur relance l'apaisement : il regarde.
        _bubbleSince = DateTimeOffset.UtcNow;
        _controller.PresentActivity(activityId);
        UpdateBubble();
    }

    /// <summary>Replace la bulle d'après la géométrie courante de la notch.</summary>
    private void PositionBubble() => ApplyGeometry(_controller.CurrentFootprint);

    /// <summary>
    /// Bulle à côté d'une notch accrochée, épaule contre épaule, sur le bord.
    /// Elle suit la largeur de la notch image par image, sans ressort propre :
    /// c'est le ressort de la notch qui la pousse.
    /// </summary>
    private void PlaceBubbleAttached(DisplayInfo display, int notchX, IslandFootprint footprint)
    {
        if (!_bubble.IsShown)
        {
            return;
        }

        double scale = display.DpiScale;
        var screen = new ScreenRect(0, 0, display.Width / scale, display.Height / scale);
        var notch = new ScreenRect((notchX - display.Left) / scale, 0, footprint.Width, footprint.Height);

        ScreenRect rect = SplitPresentation.AttachedBubbleRect(notch, screen);

        _bubble.Place(
            display.Left + (int)Math.Round(rect.X * scale),
            display.Top,
            (int)Math.Round(rect.Width * scale),
            (int)Math.Round(rect.Height * scale),
            SplitPresentation.AttachedBubble,
            floating: false);

        _bubbleSpring.Snap(rect.CenterX, rect.CenterY);
    }

    /// <summary>
    /// Bulle derrière une notch détachée : elle vise la place à côté de la
    /// pastille et y arrive par son propre ressort, un peu en retard.
    /// </summary>
    private void PlaceBubbleFloating(DisplayInfo display, ScreenRect pill, ScreenRect work)
    {
        if (!_bubble.IsShown)
        {
            return;
        }

        ScreenRect target = SplitPresentation.FloatingBubbleRect(pill, work);
        _bubbleSpring.SetTarget(target.CenterX, target.CenterY);

        if (!UseSpringAnimations() || !_detachFramesHooked)
        {
            _bubbleSpring.Snap(target.CenterX, target.CenterY);
        }
        else
        {
            _bubbleSpring.Step(Math.Clamp(_detachClock.Elapsed.TotalSeconds - _bubbleStepAt, 0, 1.0 / 15));
        }

        _bubbleStepAt = _detachClock.Elapsed.TotalSeconds;

        double scale = display.DpiScale;
        IslandFootprint size = SplitPresentation.FloatingBubble;
        ScreenRect rect = ScreenRect.Centered(_bubbleSpring.X, _bubbleSpring.Y, size.Width, size.Height);

        _bubble.Place(
            display.Left + (int)Math.Round(rect.X * scale),
            display.Top + (int)Math.Round(rect.Y * scale),
            (int)Math.Round(rect.Width * scale),
            (int)Math.Round(rect.Height * scale),
            size,
            floating: true);
    }

    private double _bubbleStepAt;
}
