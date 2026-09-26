using System.Collections.Generic;
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

    /// <summary>Naissance de la bulle en goutte : 0 fondue dans la notch, 1 à sa place.</summary>
    private double _bubbleGoo = 1;

    /// <summary>Vitesse de la goutte de la bulle, par seconde ; nulle au repos.</summary>
    private double _bubbleGooRate;

    /// <summary>Activité à présenter quand la bulle sera rentrée dans la notch : l'échange.</summary>
    private string? _bubbleSwapTo;

    /// <summary>Vrai quand la bulle rentre dans la notch pour disparaître.</summary>
    private bool _bubbleLeaving;

    /// <summary>Vrai quand la bulle naît, rentre et s'échange par la goutte.</summary>
    private bool AnimateBubbleGoo => UseSpringAnimations() && _settings.GooEnabled;

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

            // Elle rentre dans la notch par la goutte, puis disparaît.
            if (_bubble.IsShown && AnimateBubbleGoo && _bubbleSwapTo is null)
            {
                if (!_bubbleLeaving)
                {
                    _bubbleLeaving = true;
                    _bubbleGooRate = -1 / BubbleBridge.MergeSeconds;
                    HookDetachFrames();
                }

                return;
            }

            _bubble.HideBubble();
            _bubbleGoo = 1;
            _bubbleGooRate = 0;
            return;
        }

        // Une bulle qui rentrait et qui doit finalement rester ressort.
        if (_bubbleLeaving)
        {
            _bubbleLeaving = false;
            _bubbleGooRate = 1 / BubbleBridge.BirthSeconds;
            HookDetachFrames();
        }

        if (!string.Equals(_bubble.ActivityId, target!.Id, StringComparison.Ordinal))
        {
            _bubbleSince = DateTimeOffset.UtcNow;
        }

        HypnoticPreset preset = HypnoticField.Resolve(target.MotionState, target.MotionPreset);
        bool resting = HypnoticAttenuation.ShouldRest(preset, DateTimeOffset.UtcNow - _bubbleSince, NotchPresentation.Compact);

        bool firstShow = !_bubble.IsShown;
        bool goo = AnimateBubbleGoo;
        _bubble.UseSpringAnimations = UseSpringAnimations();

        if (firstShow && goo)
        {
            // Elle naît du flanc de la notch, comme une goutte qui s'en détache.
            _bubbleGoo = 0;
            _bubbleGooRate = 1 / BubbleBridge.BirthSeconds;
        }

        if (firstShow)
        {
            // Placée avant d'être montrée : sa première image est à sa place,
            // pas à la taille et à la position par défaut d'une fenêtre neuve.
            // Le ressort de suivi part de la notch : la bulle en sort.
            ScreenRect origin = UsesFloatingGeometry ? CurrentPillRect() : default;
            _bubbleSpring.Snap(origin.Right, origin.CenterY);
            PositionBubble();
        }

        _bubble.Show(target, preset, AnimateHypnotic() && !resting, ownAnimation: !goo);

        if (firstShow && goo)
        {
            HookDetachFrames();
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

        if (AnimateBubbleGoo)
        {
            // L'échange passe par la goutte : la bulle rentre dans la notch, les
            // rôles s'inversent au moment où elles ne font qu'une, puis l'autre
            // activité ressort en bulle.
            _bubbleSwapTo = activityId;
            _bubbleGooRate = -1 / BubbleBridge.MergeSeconds;
            HookDetachFrames();
            return;
        }

        _controller.PresentActivity(activityId);
        UpdateBubble();
    }

    /// <summary>Avance la goutte de la bulle ; appelée à chaque image tant qu'elle bouge.</summary>
    private void StepBubbleGoo(double dt)
    {
        if (_bubbleGooRate == 0)
        {
            return;
        }

        _bubbleGoo = Math.Clamp(_bubbleGoo + (_bubbleGooRate * dt), 0, 1);

        if (_bubbleGooRate > 0 && _bubbleGoo >= 1)
        {
            _bubbleGooRate = 0;
            return;
        }

        if (_bubbleGooRate < 0 && _bubbleGoo <= 0)
        {
            _bubbleGooRate = 0;

            if (_bubbleSwapTo is { } swap)
            {
                // Fondues : la notch prend l'activité de la bulle, et l'autre
                // ressort. L'échange n'a pas de creux propre : c'est la goutte.
                _bubbleSwapTo = null;
                _controller.PresentActivity(swap);
                UpdateBubble();

                if (_bubble.IsShown)
                {
                    _bubbleGooRate = 1 / BubbleBridge.BirthSeconds;
                }

                return;
            }

            if (_bubbleLeaving)
            {
                _bubbleLeaving = false;
                _bubble.HideBubble(immediate: true);
                _bubbleGoo = 1;
            }
        }
    }

    /// <summary>
    /// Pose la bulle à côté d'une forme — notch, languette ou pastille — en
    /// tenant compte de sa naissance : tant que la goutte la relie à la forme,
    /// la fenêtre de la bulle couvre aussi le fil.
    /// </summary>
    private void PlaceBubble(
        DisplayInfo display,
        ScreenRect owner,
        ScreenRect final,
        bool floating,
        NotchEdge edge,
        double ownerShoulder = 0,
        double bubbleShoulder = 0)
    {
        double scale = display.DpiScale;
        ScreenRect body = final;
        Microsoft.UI.Xaml.Media.Geometry? bridge = null;
        ScreenRect bounds = final;
        double contentOpacity = 1;

        IReadOnlyList<ShapePoint[]> pieces = [];

        if (_bubbleGoo < 1)
        {
            BubbleBridgeFrame frame = BubbleBridge.Frame(owner, final, _bubbleGoo, ownerShoulder, bubbleShoulder);
            body = frame.Bubble;
            bounds = body;
            pieces = frame.Bridge;
            contentOpacity = Math.Clamp((_bubbleGoo - 0.35) / 0.4, 0, 1);

            foreach (ShapePoint[] piece in pieces)
            {
                foreach (ShapePoint point in piece)
                {
                    bounds = Union(bounds, new ScreenRect(point.X, point.Y, 0, 0));
                }
            }
        }

        int x = display.Left + (int)Math.Floor(bounds.X * scale);
        int y = display.Top + (int)Math.Floor(bounds.Y * scale);
        int right = display.Left + (int)Math.Ceiling(bounds.Right * scale);
        int bottom = display.Top + (int)Math.Ceiling(bounds.Bottom * scale);

        double originX = (x - display.Left) / scale;
        double originY = (y - display.Top) / scale;

        if (pieces.Count > 0)
        {
            bridge = Composition.IslandGeometryFactory.FromPolygons(pieces, originX, originY);
        }

        _bubble.Place(
            x,
            y,
            Math.Max(1, right - x),
            Math.Max(1, bottom - y),
            new IslandFootprint(Math.Max(1, body.Width), Math.Max(1, body.Height)),
            floating,
            edge,
            _settings.SideShoulderRadius,
            body.X - originX,
            body.Y - originY,
            bridge,
            contentOpacity);
    }

    /// <summary>Replace la bulle d'après la géométrie courante de la notch.</summary>
    private void PositionBubble() => ApplyGeometry(_controller.CurrentFootprint);

    /// <summary>
    /// Bulle à côté d'une notch accrochée, épaule contre épaule, sur le même
    /// bord : à droite en haut, en dessous sur un côté. Elle suit la notch image
    /// par image, sans ressort propre : c'est le ressort de la notch qui la pousse.
    /// </summary>
    /// <param name="display">Écran de la notch.</param>
    /// <param name="notch">Notch, en DIPs relatifs à l'écran.</param>
    /// <param name="drawn">Encombrement dessiné de la notch.</param>
    private void PlaceBubbleAttached(DisplayInfo display, ScreenRect notch, IslandFootprint drawn)
    {
        if (!_bubble.IsShown)
        {
            return;
        }

        double scale = display.DpiScale;
        var screen = new ScreenRect(0, 0, display.Width / scale, display.Height / scale);
        IslandFootprint bubble = EdgeFrame.IsSide(_edge)
            ? SplitPresentation.SideBubbleOf(_settings.BubbleSize, _settings.TabSize, _settings.SideShoulderRadius)
            : SplitPresentation.AttachedBubbleOf(_settings.BubbleSize);

        ScreenRect owner = notch with { Width = drawn.Width, Height = drawn.Height };
        ScreenRect rect = SplitPresentation.AttachedBubbleRect(owner, screen, _edge, bubble);

        // Les épaules sont celles du bord : le fil part des corps, entre elles.
        bool side = EdgeFrame.IsSide(_edge);
        IslandFootprint localNotch = side ? new IslandFootprint(drawn.Height, drawn.Width) : drawn;
        IslandFootprint localBubble = side ? new IslandFootprint(bubble.Height, bubble.Width) : bubble;
        double notchShoulder = IslandShape.EffectiveShoulder(
            localNotch.Width, localNotch.Height, side ? _settings.SideShoulderRadius : _settings.Geometry.Shoulder);
        double bubbleShoulder = IslandShape.EffectiveShoulder(
            localBubble.Width, localBubble.Height, side ? _settings.SideShoulderRadius : SplitPresentation.BubbleShoulder);

        PlaceBubble(display, owner, rect, floating: false, _edge, notchShoulder, bubbleShoulder);

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
        IslandFootprint targetSize = SplitPresentation.FloatingBubbleOf(_settings.BubbleSize);
        target = ScreenRect.Centered(target.CenterX, target.CenterY, targetSize.Width, targetSize.Height);
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

        IslandFootprint size = SplitPresentation.FloatingBubbleOf(_settings.BubbleSize);
        ScreenRect rect = ScreenRect.Centered(_bubbleSpring.X, _bubbleSpring.Y, size.Width, size.Height);

        PlaceBubble(display, pill, rect, floating: true, NotchEdge.Top);
    }

    private double _bubbleStepAt;
}
