using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Animation;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Display;
using SpaceNotch.Platform.Windows.Win32;
using SpaceNotch_App.Composition;
using Windows.Graphics;

namespace SpaceNotch_App.Windows;

/// <summary>
/// La notch qu'on arrache au bord de l'écran. Voir ADR-019.
///
/// <para>
/// <b>Le geste.</b> Un appui qui ne bouge pas est un clic, validé au relâcher.
/// Tirée vers le bas, la notch accrochée s'allonge en résistant ; au-delà de
/// 40 DIPs elle se détache d'un coup : une goutte s'étire entre le bord et la
/// pastille, se rompt, et la trace rentre dans le bord. La pastille suit alors
/// la main avec un léger retard élastique et s'étire dans le sens de sa course.
/// Lâchée, elle garde son élan : on projette où elle s'arrêterait, et elle
/// rejoint l'aimant le plus proche — un coin, un côté, ou le haut de l'écran
/// où elle se raccroche en aspirant la goutte à l'envers. Lâchée sans élan,
/// elle reste où on l'a posée.
/// </para>
///
/// <para>
/// <b>Le coût.</b> Tout le mouvement est intégré image par image, mais
/// seulement pendant qu'il existe : l'écouteur de rendu est détaché dès que la
/// main est levée et que tout est au repos. Une notch flottante posée ne coûte
/// rien, comme une notch accrochée.
/// </para>
///
/// <para>
/// <b>Le repère.</b> Tout est calculé en DIPs, relativement au coin supérieur
/// gauche du moniteur où le geste a commencé. Ce moniteur est figé jusqu'au
/// raccrochage : la pastille reste sur son écran, et un changement d'écran la
/// raccroche.
/// </para>
/// </summary>
public sealed partial class IslandWindow
{
    /// <summary>Étape du geste en cours.</summary>
    private enum DragPhase
    {
        /// <summary>Aucun geste : la notch est posée, accrochée ou non.</summary>
        None,

        /// <summary>Bouton enfoncé, pas encore sorti de la tolérance du clic.</summary>
        Pressed,

        /// <summary>Notch accrochée qu'on tire vers le bas.</summary>
        Pulling,

        /// <summary>Tirage relâché avant l'arrachement : la notch remonte.</summary>
        Unpulling,

        /// <summary>Pastille tenue en main.</summary>
        Dragging,

        /// <summary>Pastille lâchée qui rejoint sa destination.</summary>
        Settling
    }

    /// <summary>La pastille suit la main : un peu en retard, un rebond à l'arrêt.</summary>
    private static readonly SpringParameters FollowSpring = SpringParameters.FromResponse(0.22, 0.62);

    /// <summary>La pastille lancée : le ressort d'élan recommandé par Apple, amortissement 0,8.</summary>
    private static readonly SpringParameters ThrowSpring = SpringParameters.FromResponse(0.42, 0.8);

    /// <summary>Le « pop » de l'arrachement : un rebond franc et bref.</summary>
    private static readonly SpringParameters PopSpring = SpringParameters.FromResponse(0.32, 0.45);

    /// <summary>La notch tirée puis relâchée remonte d'un coup de ressort.</summary>
    private static readonly SpringParameters UnpullSpring = SpringParameters.FromResponse(0.30, 0.6);

    /// <summary>La bulle qui suit une notch détachée traîne un peu plus que la notch.</summary>
    private static readonly SpringParameters BubbleFollowSpring = SpringParameters.FromResponse(0.30, 0.7);

    /// <summary>Échelle de la pastille à l'instant où elle se détache.</summary>
    private const double PopFrom = 0.9;

    private readonly Stopwatch _detachClock = new();
    private readonly VelocityTracker _velocity = new();
    private readonly Spring2D _pillSpring = new(FollowSpring);
    private readonly Spring1D _pop = new(PopSpring, 1);
    private readonly Spring1D _pullSpring = new(UnpullSpring, 0, restDistance: 0.2, restSpeed: 2);
    private readonly Spring2D _bubbleSpring = new(BubbleFollowSpring);

    private NotchAttachment _attachment = NotchAttachment.Attached;
    private DragPhase _dragPhase = DragPhase.None;

    /// <summary>Moniteur figé pendant le geste et tant que la notch flotte.</summary>
    private DisplayInfo? _detachDisplay;

    /// <summary>Pastille au repos, en DIPs relatifs au moniteur figé.</summary>
    private ScreenRect _pill;

    /// <summary>Destination du dernier lâcher.</summary>
    private FloatingLanding _landing = FloatingLanding.Stay;

    /// <summary>Forme accrochée d'où la goutte part, ou vers laquelle elle revient.</summary>
    private IslandFootprint _gooAttached = IslandFootprint.Idle;

    /// <summary>Avancement de la goutte : 0 fusionnée, 1 séparée.</summary>
    private double _goo = 1;

    /// <summary>Vitesse d'avancement de la goutte, par seconde. Nulle au repos.</summary>
    private double _gooRate;

    private double _pressX;
    private double _pressY;
    private double _grabX;
    private double _grabY;
    private double _pull;
    private double _lastDetachFrame;
    private bool _pointerDown;
    private bool _caughtInFlight;
    private bool _detachFramesHooked;
    private Pointer? _capturedPointer;

    /// <summary>Vrai quand la fenêtre doit être posée selon la géométrie flottante.</summary>
    private bool UsesFloatingGeometry => _attachment == NotchAttachment.Floating;

    /// <summary>Vrai tant que la goutte relie encore quelque chose au bord.</summary>
    private bool GooActive => _gooRate != 0 || _goo < 1;

    /// <summary>Vrai tant qu'une main tient ou vient de lâcher la notch : l'aperçu au survol n'a pas lieu.</summary>
    private bool IsDragging => _dragPhase is DragPhase.Pulling or DragPhase.Dragging or DragPhase.Settling;

    // ------------------------------------------------------------------
    // Geste
    // ------------------------------------------------------------------

    /// <summary>
    /// Début d'un geste possible : rien ne bouge encore, et rien n'est décidé.
    /// Le clic n'est validé qu'au relâcher, le glisser qu'au-delà de la
    /// tolérance.
    /// </summary>
    private void BeginPress(PointerRoutedEventArgs e)
    {
        _detachDisplay ??= ResolveDisplay();

        (double x, double y) = CursorDip();

        _pressX = x;
        _pressY = y;
        _pull = 0;
        _pointerDown = true;
        _velocity.Reset();

        // Une pastille rattrapée en plein vol s'arrête dans la main, là où elle
        // est ; le raccrochage en cours est annulé. Posée, elle est reprise à sa
        // place exacte, aperçu compris.
        _caughtInFlight = UsesFloatingGeometry && _dragPhase == DragPhase.Settling;

        if (_caughtInFlight)
        {
            _pillSpring.SetVelocity(0, 0);
            _landing = FloatingLanding.Stay;
        }
        else if (UsesFloatingGeometry)
        {
            ScreenRect rest = CurrentPillRect();
            _pillSpring.Snap(rest.CenterX, rest.CenterY);
        }

        if (_gooRate < 0)
        {
            _gooRate = 1 / GooBridge.TearSeconds;
        }

        _dragPhase = DragPhase.Pressed;

        if (IslandBody.CapturePointer(e.Pointer))
        {
            _capturedPointer = e.Pointer;
        }

        HookDetachFrames();
    }

    private void OnIslandPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerDown)
        {
            return;
        }

        e.Handled = true;
        EndPress(click: true);
    }

    private void OnIslandPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Une capture perdue — une autre fenêtre qui s'impose, un menu système —
        // vaut un lâcher, jamais un clic : l'utilisateur n'a rien validé.
        if (_pointerDown)
        {
            EndPress(click: false);
        }
    }

    private void EndPress(bool click)
    {
        _pointerDown = false;

        if (_capturedPointer is not null)
        {
            IslandBody.ReleasePointerCapture(_capturedPointer);
            _capturedPointer = null;
        }

        double now = _detachClock.Elapsed.TotalSeconds;

        switch (_dragPhase)
        {
            case DragPhase.Pressed:
                _dragPhase = DragPhase.None;

                if (!UsesFloatingGeometry)
                {
                    _detachDisplay = null;
                }
                else if (_caughtInFlight)
                {
                    // Rattrapée puis simplement relâchée : elle se pose là.
                    _pill = NominalPillAt(_pillSpring.X, _pillSpring.Y);
                    _pillSpring.SetTarget(_pillSpring.X, _pillSpring.Y);
                }

                if (click)
                {
                    CommitClick();
                }

                break;

            case DragPhase.Pulling:
                (_, double pullVelocity) = _velocity.Velocity(now);

                if (UseSpringAnimations())
                {
                    _pullSpring.Snap(_pull);
                    _pullSpring.SetTarget(0);
                    _pullSpring.SetVelocity(pullVelocity);
                    _dragPhase = DragPhase.Unpulling;
                }
                else
                {
                    _pull = 0;
                    _dragPhase = DragPhase.None;
                    _detachDisplay = null;
                    ApplyGeometry(_controller.CurrentFootprint);
                }

                break;

            case DragPhase.Dragging:
                Release(_velocity.Velocity(now));
                break;
        }

        HookDetachFrames();
    }

    /// <summary>
    /// Lâcher d'une pastille tenue : l'élan de la main est transmis au ressort,
    /// qui l'amène vers sa destination sans raccord visible.
    /// </summary>
    private void Release((double X, double Y) velocity)
    {
        DisplayInfo display = DetachDisplay();
        ScreenRect work = WorkAreaDip(display);
        ScreenRect pill = NominalPillAt(_pillSpring.X, _pillSpring.Y);

        FloatingTarget target = Detachment.Land(pill, velocity.X, velocity.Y, work, AttachCenterX(display));

        _landing = target.Landing;

        // Le raccrochage vise le bord du moniteur, là où la notch vit
        // accrochée — pas celui de la zone de travail, qui peut commencer sous
        // une barre des tâches placée en haut.
        double targetY = target.Landing == FloatingLanding.Reattach ? 0 : target.Y;

        _pillSpring.SetParameters(ThrowSpring);
        _pillSpring.SetTarget(target.X + (pill.Width / 2), targetY + (pill.Height / 2));

        if (!UseSpringAnimations())
        {
            _pillSpring.Snap(_pillSpring.TargetX, _pillSpring.TargetY);
            SettleLanding(immediate: true);
            return;
        }

        _pillSpring.SetVelocity(velocity.X, velocity.Y);
        _dragPhase = DragPhase.Settling;
    }

    /// <summary>La notch accrochée cède : elle devient une pastille, et une goutte la relie encore au bord.</summary>
    private void BeginTear()
    {
        DisplayInfo display = DetachDisplay();
        NotchGeometry geometry = _settings.Geometry;
        bool animate = UseSpringAnimations();

        IslandFootprint attached = _controller.CurrentFootprint;
        IslandFootprint pulled = Detachment.Pulled(attached, _pull);
        IslandFootprint body = Detachment.FloatingOf(_controller.CollapsedFootprint, geometry.Shoulder);
        double attachCenterX = AttachCenterX(display);

        // La prise est conservée : la pastille reste sous le doigt à l'endroit
        // exact où la notch a été saisie.
        _grabX = _pressX - attachCenterX;
        _grabY = _pressY - (body.Height / 2);

        // Elle naît au bas de la forme étirée, là où la matière était, puis
        // rejoint la main.
        _pillSpring.SetParameters(FollowSpring);
        _pillSpring.Snap(attachCenterX, pulled.Height - (body.Height / 2));

        _pop.Snap(animate ? PopFrom : 1);
        _pop.SetTarget(1);

        _gooAttached = attached;
        _goo = animate ? 0 : 1;
        _gooRate = animate ? 1 / GooBridge.TearSeconds : 0;

        _attachment = NotchAttachment.Floating;
        _dragPhase = DragPhase.Dragging;
        _pull = 0;

        _previewEnterTimer?.Stop();
        _hoverExpandTimer?.Stop();
        _controller.EndPreview();

        EnterFloatingLayout();
        MiniLog("détachée");
    }

    /// <summary>Reprise d'une pastille déjà flottante.</summary>
    private void BeginFloatingDrag()
    {
        _grabX = _pressX - _pillSpring.X;
        _grabY = _pressY - _pillSpring.Y;

        _pillSpring.SetParameters(FollowSpring);
        _dragPhase = DragPhase.Dragging;

        _previewEnterTimer?.Stop();
        _hoverExpandTimer?.Stop();
        _controller.EndPreview();
    }

    /// <summary>
    /// Cible du ressort pendant que la main tient la pastille : sous la main, à
    /// la prise près, avec une résistance élastique au-delà des bords de
    /// l'écran — la pastille ne sort pas, elle rechigne.
    /// </summary>
    private void FollowPointer(double x, double y)
    {
        DisplayInfo display = DetachDisplay();
        ScreenRect work = WorkAreaDip(display);
        ScreenRect pill = NominalPillAt(0, 0);

        double halfWidth = pill.Width / 2;
        double halfHeight = pill.Height / 2;

        double targetX = x - _grabX;
        double targetY = y - _grabY;

        targetX = Resist(targetX, work.X + halfWidth, work.Right - halfWidth, pill.Width);

        // Le haut de l'écran reste atteignable même sous une barre des tâches
        // placée en haut : c'est là que la notch se raccroche.
        targetY = Resist(targetY, halfHeight, work.Bottom - halfHeight, pill.Height);

        _pillSpring.SetTarget(targetX, targetY);

        if (!UseSpringAnimations())
        {
            _pillSpring.Snap(targetX, targetY);
        }
    }

    private static double Resist(double value, double min, double max, double dimension)
    {
        if (value < min)
        {
            return min + FluidMotion.RubberBand(value - min, dimension);
        }

        if (value > max)
        {
            return max + FluidMotion.RubberBand(value - max, dimension);
        }

        return value;
    }

    /// <summary>
    /// La pastille est arrivée. Sur un aimant ou à l'endroit où elle a été
    /// posée, elle y reste ; au bord, elle se raccroche.
    /// </summary>
    private void SettleLanding(bool immediate)
    {
        if (_landing == FloatingLanding.Reattach)
        {
            if (immediate)
            {
                FinishReattach();
            }

            return;
        }

        _pill = NominalPillAt(_pillSpring.X, _pillSpring.Y);
        _dragPhase = DragPhase.None;
        _presence.Recheck();
        ApplyGeometry(_controller.CurrentFootprint);
    }

    /// <summary>
    /// La notch retrouve le bord : la goutte a fini de l'aspirer, elle est de
    /// nouveau la notch accrochée de référence.
    /// </summary>
    private void FinishReattach()
    {
        _attachment = NotchAttachment.Attached;
        _dragPhase = DragPhase.None;
        _landing = FloatingLanding.Stay;
        _goo = 1;
        _gooRate = 0;
        _pop.Snap(1);
        _pillSpring.SetParameters(FollowSpring);

        ExitFloatingLayout();
        MiniLog("raccrochée");
    }

    /// <summary>
    /// Raccroche la notch depuis le menu : elle vole jusqu'au bord et la goutte
    /// l'aspire, exactement comme après un lancer vers le haut.
    /// </summary>
    private void ReattachFromMenu()
    {
        if (!UsesFloatingGeometry || _pointerDown)
        {
            return;
        }

        if (!UseSpringAnimations())
        {
            ForceAttach();
            return;
        }

        if (_dragPhase == DragPhase.None)
        {
            ScreenRect rest = CurrentPillRect();
            _pillSpring.Snap(rest.CenterX, rest.CenterY);
        }

        _controller.RequestCollapse();

        ScreenRect pill = NominalPillAt(0, 0);
        _landing = FloatingLanding.Reattach;
        _pillSpring.SetParameters(ThrowSpring);
        _pillSpring.SetTarget(AttachCenterX(DetachDisplay()), pill.Height / 2);
        _dragPhase = DragPhase.Settling;

        HookDetachFrames();
    }

    /// <summary>
    /// Ramène la notch au bord sans animation : changement d'écran, réglage
    /// désactivé, arrêt. La notch accrochée est la forme de référence ; tout
    /// ce qui rend la position flottante incertaine y ramène.
    /// </summary>
    private void ForceAttach()
    {
        if (!UsesFloatingGeometry && _dragPhase == DragPhase.None)
        {
            _detachDisplay = null;
            return;
        }

        _pointerDown = false;

        if (_capturedPointer is not null)
        {
            IslandBody.ReleasePointerCapture(_capturedPointer);
            _capturedPointer = null;
        }

        _pull = 0;
        FinishReattach();
        HookDetachFrames();
    }

    // ------------------------------------------------------------------
    // Image par image
    // ------------------------------------------------------------------

    private void HookDetachFrames()
    {
        bool needed = !_isClosed && NeedsDetachFrames();

        if (needed && !_detachFramesHooked)
        {
            _detachFramesHooked = true;

            if (!_detachClock.IsRunning)
            {
                _detachClock.Start();
            }

            _lastDetachFrame = _detachClock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnDetachFrame;
        }
        else if (!needed && _detachFramesHooked)
        {
            _detachFramesHooked = false;
            CompositionTarget.Rendering -= OnDetachFrame;
        }
    }

    private bool NeedsDetachFrames()
        => _dragPhase != DragPhase.None
            || _gooRate != 0
            || !_pop.IsSettled
            || (UsesFloatingGeometry && _bubble.IsShown && !_bubbleSpring.IsSettled);

    private void OnDetachFrame(object? sender, object e)
    {
        if (_isClosed)
        {
            HookDetachFrames();
            return;
        }

        double now = _detachClock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastDetachFrame, 0, 1.0 / 15);
        _lastDetachFrame = now;

        (double x, double y) = CursorDip();

        if (_pointerDown)
        {
            _velocity.Add(now, x, y);
        }

        switch (_dragPhase)
        {
            case DragPhase.Pressed when Detachment.ExceedsClickSlop(x - _pressX, y - _pressY):
                if (UsesFloatingGeometry)
                {
                    BeginFloatingDrag();
                }
                else
                {
                    // Même quand le détachement est désactivé, la notch tirée
                    // s'allonge et résiste : elle dit qu'elle est accrochée,
                    // puis remonte. Elle ne cède simplement jamais.
                    _dragPhase = DragPhase.Pulling;
                    _previewEnterTimer?.Stop();
                    _hoverExpandTimer?.Stop();
                }

                break;

            case DragPhase.Pulling:
                _pull = y - _pressY;

                if (_settings.AllowDetach && Detachment.ShouldTear(_pull))
                {
                    BeginTear();
                }

                break;

            case DragPhase.Unpulling:
                _pullSpring.Step(dt);
                _pull = Math.Max(0, _pullSpring.Value);

                if (_pullSpring.IsSettled)
                {
                    _pull = 0;
                    _dragPhase = DragPhase.None;
                    _detachDisplay = null;
                }

                break;
        }

        if (_dragPhase == DragPhase.Dragging)
        {
            FollowPointer(x, y);
        }

        if (UsesFloatingGeometry)
        {
            StepFloating(dt);
        }

        ApplyGeometry(_controller.CurrentFootprint);
        HookDetachFrames();
    }

    private void StepFloating(double dt)
    {
        if (_dragPhase is DragPhase.Dragging or DragPhase.Settling)
        {
            _pillSpring.Step(dt);
        }

        _pop.Step(dt);

        if (_gooRate != 0)
        {
            _goo = Math.Clamp(_goo + (_gooRate * dt), 0, 1);

            if (_goo >= 1 && _gooRate > 0)
            {
                _gooRate = 0;
            }
        }

        if (_dragPhase != DragPhase.Settling)
        {
            return;
        }

        if (_landing != FloatingLanding.Reattach)
        {
            if (_pillSpring.IsSettled)
            {
                SettleLanding(immediate: false);
            }

            return;
        }

        // Raccrochage : quand la pastille arrive au bord, la goutte l'aspire —
        // la même fonction qu'à l'arrachement, jouée à l'envers.
        ScreenRect pill = NominalPillAt(_pillSpring.X, _pillSpring.Y);

        if (_gooRate == 0 && _goo >= 1 && pill.Y < _controller.CollapsedFootprint.Height)
        {
            _gooAttached = _controller.CollapsedFootprint;
            _gooRate = -1 / GooBridge.ReattachSeconds;
        }

        bool arrived = Math.Abs(_pillSpring.X - _pillSpring.TargetX) < 1.5
            && Math.Abs(_pillSpring.Y - _pillSpring.TargetY) < 1.5;

        if (_goo <= 0 && arrived)
        {
            FinishReattach();
        }
    }

    // ------------------------------------------------------------------
    // Géométrie flottante
    // ------------------------------------------------------------------

    /// <summary>
    /// Pose la fenêtre pour une notch détachée : la pastille, et tant qu'elle
    /// existe la goutte qui la relie au bord. La fenêtre couvre exactement ce
    /// qui est dessiné.
    /// </summary>
    private void ApplyFloatingGeometry(IslandFootprint footprint)
    {
        DisplayInfo display = DetachDisplay();
        double scale = display.DpiScale;
        NotchGeometry geometry = _settings.Geometry;
        ScreenRect work = WorkAreaDip(display);

        IslandFootprint body = Detachment.FloatingOf(footprint, geometry.Shoulder);
        bool moving = _dragPhase is DragPhase.Dragging or DragPhase.Settling || !_pop.IsSettled || GooActive;

        ScreenRect pill;

        if (moving)
        {
            (double sx, double sy) = UseSpringAnimations()
                ? FluidMotion.Stretch(_pillSpring.VelocityX, _pillSpring.VelocityY)
                : (1, 1);

            double pop = _pop.Value;
            pill = ScreenRect.Centered(_pillSpring.X, _pillSpring.Y, body.Width * sx * pop, body.Height * sy * pop);
        }
        else
        {
            pill = Detachment.Anchor(_pill, body, work);
        }

        // La trace et le fil de la goutte.
        List<ShapePoint[]>? goo = null;
        ScreenRect bounds = pill;

        if (GooActive)
        {
            IslandFootprint residue = GooBridge.Residue(_gooAttached, _goo);
            var residueRect = new ScreenRect(AttachCenterX(display) - (residue.Width / 2), 0, residue.Width, residue.Height);

            goo = [];

            if (residue.Height > 0.5)
            {
                goo.Add(geometry.Silhouette(residue)
                    .Select(p => new ShapePoint(p.X + residueRect.X, p.Y + residueRect.Y))
                    .ToArray());

                bounds = Union(bounds, residueRect);
            }

            goo.AddRange(GooBridge.Neck(residueRect, pill, _goo));
        }

        int winX = display.Left + (int)Math.Floor(bounds.X * scale);
        int winY = display.Top + (int)Math.Floor(bounds.Y * scale);
        int winRight = display.Left + (int)Math.Ceiling(bounds.Right * scale);
        int winBottom = display.Top + (int)Math.Ceiling(bounds.Bottom * scale);

        MoveWindow(winX, winY, Math.Max(1, winRight - winX), Math.Max(1, winBottom - winY), recheck: !moving);

        // L'origine du contenu, en DIPs de moniteur : c'est elle qui aligne au
        // pixel près la pastille, la goutte et la fenêtre.
        double originX = (winX - display.Left) / scale;
        double originY = (winY - display.Top) / scale;

        IslandBody.Margin = new Thickness(pill.X - originX, pill.Y - originY, 0, 0);
        IslandBody.Width = pill.Width;
        IslandBody.Height = pill.Height;

        // Le contenu garde sa taille nominale au centre de la matière qui
        // s'étire : c'est la surface qui se déforme, jamais le texte.
        ContentArea.Width = body.Width;
        ContentArea.Height = body.Height;

        var drawn = new IslandFootprint(pill.Width, pill.Height);
        double radius = geometry.FloatingRadiusFor(drawn);

        if (_shape.Build(drawn, radius, geometry.Smoothing, floating: true) is { } silhouette)
        {
            SurfaceFill.Data = silhouette;
        }

        if (goo is { Count: > 0 })
        {
            GooFill.Data = IslandGeometryFactory.FromPolygons(goo, originX, originY);
            GooFill.Visibility = Visibility.Visible;
        }
        else
        {
            GooFill.Visibility = Visibility.Collapsed;
        }

        _atmosphere?.PositionAround(new AtmospherePlacement(
            display.Left + (int)Math.Round(pill.X * scale),
            display.Top + (int)Math.Round(pill.Y * scale),
            (int)Math.Round(pill.Width * scale),
            (int)Math.Round(pill.Height * scale),
            pill.Width,
            pill.Height,
            radius,
            0,
            0,
            Floating: true));

        PlaceBubbleFloating(display, pill, work);
    }

    /// <summary>Pastille de repos centrée en un point, à sa taille nominale.</summary>
    private ScreenRect NominalPillAt(double centerX, double centerY)
    {
        IslandFootprint body = Detachment.FloatingOf(_controller.CollapsedFootprint, _settings.Geometry.Shoulder);
        return ScreenRect.Centered(centerX, centerY, body.Width, body.Height);
    }

    /// <summary>Pastille telle qu'elle est posée en ce moment, hors mouvement.</summary>
    private ScreenRect CurrentPillRect()
    {
        IslandFootprint body = Detachment.FloatingOf(_controller.CurrentFootprint, _settings.Geometry.Shoulder);
        return Detachment.Anchor(_pill, body, WorkAreaDip(DetachDisplay()));
    }

    private void EnterFloatingLayout()
    {
        IslandBody.HorizontalAlignment = HorizontalAlignment.Left;
        IslandBody.VerticalAlignment = VerticalAlignment.Top;

        // Plus d'épaules : le contenu occupe toute la pastille, centré.
        _contentShoulder = 0;
        ContentArea.Margin = new Thickness(0);
        ContentArea.HorizontalAlignment = HorizontalAlignment.Center;
        ContentArea.VerticalAlignment = VerticalAlignment.Center;

        SpecularFill.Visibility = Visibility.Collapsed;
        GooFill.Fill = SurfaceFill.Fill;

        _shape.Forget();
    }

    private void ExitFloatingLayout()
    {
        IslandBody.HorizontalAlignment = HorizontalAlignment.Center;
        IslandBody.VerticalAlignment = VerticalAlignment.Top;
        IslandBody.Margin = new Thickness(0);

        ContentArea.Width = double.NaN;
        ContentArea.Height = double.NaN;
        ContentArea.HorizontalAlignment = HorizontalAlignment.Stretch;
        ContentArea.VerticalAlignment = VerticalAlignment.Stretch;

        // Force la reprise de la marge d'épaules au prochain tracé.
        _contentShoulder = double.NaN;

        SpecularFill.Visibility = Visibility.Visible;
        GooFill.Visibility = Visibility.Collapsed;
        GooFill.Data = null;

        _shape.Forget();
        _detachDisplay = null;

        ApplyGeometry(_controller.CurrentFootprint);
    }

    // ------------------------------------------------------------------
    // Repères
    // ------------------------------------------------------------------

    private DisplayInfo DetachDisplay() => _detachDisplay ??= ResolveDisplay();

    /// <summary>Position du pointeur en DIPs, relative au moniteur figé.</summary>
    private (double X, double Y) CursorDip()
    {
        DisplayInfo display = DetachDisplay();
        double scale = display.DpiScale;

        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT point))
        {
            return (_pressX, _pressY);
        }

        return ((point.X - display.Left) / scale, (point.Y - display.Top) / scale);
    }

    /// <summary>Zone de travail du moniteur, en DIPs relatifs à son coin supérieur gauche.</summary>
    private static ScreenRect WorkAreaDip(DisplayInfo display)
    {
        double scale = display.DpiScale;

        return new ScreenRect(
            (display.WorkLeft - display.Left) / scale,
            (display.WorkTop - display.Top) / scale,
            display.WorkWidth / scale,
            display.WorkHeight / scale);
    }

    /// <summary>Abscisse du centre de la notch accrochée, en DIPs relatifs au moniteur.</summary>
    private double AttachCenterX(DisplayInfo display)
        => (display.Width / display.DpiScale / 2) + _settings.HorizontalOffset;

    private static ScreenRect Union(ScreenRect a, ScreenRect b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);

        return new ScreenRect(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }

    /// <summary>
    /// Déplace la fenêtre, sans resoumettre un rectangle identique. La présence
    /// plein écran n'est relue qu'au repos : relire à chaque image d'un glisser
    /// n'apprendrait rien de plus.
    /// </summary>
    private void MoveWindow(int x, int y, int width, int height, bool recheck)
    {
        if (x == _lastWindowX && y == _lastWindowY && width == _lastWindowWidth && height == _lastWindowHeight)
        {
            return;
        }

        _lastWindowX = x;
        _lastWindowY = y;
        _lastWindowWidth = width;
        _lastWindowHeight = height;

        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));

        if (recheck)
        {
            _presence.Recheck();
        }
    }

    private static void MiniLog(string state) => SpaceNotch.Infrastructure.Logging.MiniLogger.Log($"[NOTCH] {state}");
}
