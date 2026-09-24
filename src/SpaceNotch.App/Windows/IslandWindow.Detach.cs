using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Animation;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Infrastructure.Config;
using SpaceNotch.Platform.Windows.Display;
using SpaceNotch.Platform.Windows.Win32;
using SpaceNotch_App.Composition;
using Windows.Graphics;

namespace SpaceNotch_App.Windows;

/// <summary>
/// La notch qu'on arrache à son bord, qu'on promène d'un écran à l'autre, et
/// qu'on raccroche en haut, à gauche ou à droite. Voir ADR-019 et ADR-020.
///
/// <para>
/// <b>Le geste.</b> Un appui qui ne bouge pas est un clic, validé au relâcher.
/// Tirée vers l'intérieur de l'écran, la notch accrochée s'allonge en
/// résistant ; au-delà de la distance d'arrachement elle se détache d'un
/// coup : une goutte s'étire entre le bord et la pastille, se rompt, et la
/// trace rentre dans le bord. La pastille suit alors la main avec un léger
/// retard élastique et s'étire dans le sens de sa course. Lâchée, elle garde
/// son élan : on projette où elle s'arrêterait, et elle rejoint un aimant de
/// coin, ou un bord où elle se raccroche en aspirant la goutte à l'envers.
/// Lâchée sans élan, elle reste où on l'a posée ; un double-clic la renvoie au
/// dernier bord où elle était accrochée.
/// </para>
///
/// <para>
/// <b>Les bords.</b> Toute la géométrie accrochée est calculée comme en haut,
/// dans le repère du bord (<see cref="EdgeFrame"/>), puis tournée : la
/// languette de gauche est la notch du haut, couchée.
/// </para>
///
/// <para>
/// <b>Les écrans.</b> Tout est calculé en DIPs, relativement au coin supérieur
/// gauche de l'écran où se trouve la pastille. Poussée vers un écran voisin,
/// elle résiste d'abord, puis passe : le repère change, l'échelle aussi, la
/// position physique ne bouge pas.
/// </para>
///
/// <para>
/// <b>Le coût.</b> Tout le mouvement est intégré image par image, mais
/// seulement pendant qu'il existe : au repos, aucun écouteur de rendu.
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

        /// <summary>Notch accrochée qu'on tire vers l'intérieur.</summary>
        Pulling,

        /// <summary>Tirage relâché avant l'arrachement : la notch revient au bord.</summary>
        Unpulling,

        /// <summary>Pastille tenue en main.</summary>
        Dragging,

        /// <summary>Pastille lâchée qui rejoint sa destination.</summary>
        Settling
    }

    /// <summary>La pastille lancée : le ressort d'élan recommandé par Apple, amortissement 0,8.</summary>
    private static readonly SpringParameters ThrowSpring = SpringParameters.FromResponse(0.42, 0.8);

    /// <summary>Le « pop » de l'arrachement : un rebond franc et bref.</summary>
    private static readonly SpringParameters PopSpring = SpringParameters.FromResponse(0.32, 0.45);

    /// <summary>La notch tirée puis relâchée revient d'un coup de ressort.</summary>
    private static readonly SpringParameters UnpullSpring = SpringParameters.FromResponse(0.30, 0.6);

    /// <summary>La bulle qui suit une notch détachée traîne un peu plus que la notch.</summary>
    private static readonly SpringParameters BubbleFollowSpring = SpringParameters.FromResponse(0.30, 0.7);

    /// <summary>Échelle de la pastille à l'instant où elle se détache.</summary>
    private const double PopFrom = 0.9;

    private readonly Stopwatch _detachClock = new();
    private readonly VelocityTracker _velocity = new();
    private readonly Spring2D _pillSpring = new(SpringParameters.FromResponse(0.22, 0.62));
    private readonly Spring1D _pop = new(PopSpring, 1);
    private readonly Spring1D _pullSpring = new(UnpullSpring, 0, restDistance: 0.2, restSpeed: 2);
    private readonly Spring2D _bubbleSpring = new(BubbleFollowSpring);

    private NotchAttachment _attachment = NotchAttachment.Attached;
    private DragPhase _dragPhase = DragPhase.None;

    /// <summary>Bord où la notch est accrochée — ou le sera à nouveau.</summary>
    private NotchEdge _edge = NotchEdge.Top;

    /// <summary>Position d'une languette le long de son côté, de 0 à 1.</summary>
    private double _dockOffset = 0.5;

    /// <summary>Écran figé pendant le geste et tant que la notch flotte.</summary>
    private DisplayInfo? _detachDisplay;

    /// <summary>Pastille au repos, en DIPs relatifs à l'écran figé.</summary>
    private ScreenRect _pill;

    /// <summary>Destination du dernier lâcher.</summary>
    private FloatingTarget _landing = new(FloatingLanding.Stay, 0, 0);

    // La goutte : son bord, la forme accrochée d'où elle part (dans le repère
    // du bord), sa position le long du bord, et la taille que prend la
    // pastille à l'instant de la fusion.
    private NotchEdge _gooEdge = NotchEdge.Top;
    private IslandFootprint _gooAttached = IslandFootprint.Idle;
    private double _gooCenterU;
    private double _gooShoulder;
    private IslandFootprint _gooOrigin = IslandFootprint.Idle;

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

    /// <summary>Clic sur la pastille en attente : un second clic en fait un double-clic.</summary>
    private DispatcherQueueTimer? _pendingClickTimer;

    /// <summary>Vrai quand la fenêtre doit être posée selon la géométrie flottante.</summary>
    private bool UsesFloatingGeometry => _attachment == NotchAttachment.Floating;

    /// <summary>Vrai quand la notch est une languette accrochée à un côté.</summary>
    private bool UsesSideTab => !UsesFloatingGeometry && EdgeFrame.IsSide(_edge);

    /// <summary>Vrai tant que la goutte relie encore quelque chose au bord.</summary>
    private bool GooActive => _gooRate != 0 || _goo < 1;

    /// <summary>Vrai quand la goutte doit être jouée : animations permises et réglage actif.</summary>
    private bool AnimateGoo => UseSpringAnimations() && _settings.GooEnabled;

    /// <summary>Épaules du bord : celles de la notch en haut, celles de la languette sur un côté.</summary>
    private double ShoulderFor(NotchEdge edge)
        => EdgeFrame.IsSide(edge) ? _settings.SideShoulderRadius : _settings.Geometry.Shoulder;

    /// <summary>Reprend le bord et la position enregistrés : c'est là que la notch démarre.</summary>
    private void LoadDock()
    {
        _edge = _settings.AllowSideEdges ? _settings.DockEdge : NotchEdge.Top;
        _dockOffset = _settings.DockOffset;
    }

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
            _landing = new FloatingTarget(FloatingLanding.Stay, 0, 0);
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

                    if (click)
                    {
                        CommitClick();
                    }

                    break;
                }

                if (_caughtInFlight)
                {
                    // Rattrapée puis simplement relâchée : elle se pose là.
                    _pill = NominalPillAt(_pillSpring.X, _pillSpring.Y);
                    _pillSpring.SetTarget(_pillSpring.X, _pillSpring.Y);
                }

                if (click)
                {
                    ClickFloating();
                }

                break;

            case DragPhase.Pulling:
                double pullVelocity = PullOf(_velocity.Velocity(now));

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
    /// Clic sur la pastille. Il attend le temps d'un double-clic : un second
    /// clic la renvoie à son bord ; seul, il l'ouvre. Accrochée, la notch n'a
    /// pas de double-clic et ne fait jamais attendre son clic.
    /// </summary>
    private void ClickFloating()
    {
        if (_pendingClickTimer is { IsRunning: true })
        {
            _pendingClickTimer.Stop();
            ReattachToDock();
            return;
        }

        _pendingClickTimer ??= CreateOneShotTimer(DoubleClickDelay(), () =>
        {
            if (UsesFloatingGeometry && _dragPhase == DragPhase.None)
            {
                CommitClick();
            }
        });

        _pendingClickTimer.Interval = DoubleClickDelay();
        _pendingClickTimer.Start();
    }

    /// <summary>Délai du double-clic de Windows, borné : au-delà de 400 ms, ouvrir paraîtrait lent.</summary>
    private static TimeSpan DoubleClickDelay()
    {
        uint milliseconds = 300;

        try
        {
            milliseconds = new global::Windows.UI.ViewManagement.UISettings().DoubleClickTime;
        }
        catch (Exception)
        {
            // Réglage illisible : la valeur par défaut convient.
        }

        return TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 200u, 400u));
    }

    /// <summary>Composante d'un déplacement qui éloigne du bord : c'est elle qui tire.</summary>
    private double PullOf((double X, double Y) delta) => _edge switch
    {
        NotchEdge.Left => delta.X,
        NotchEdge.Right => -delta.X,
        _ => delta.Y
    };

    /// <summary>
    /// Lâcher d'une pastille tenue : l'élan de la main est transmis au ressort,
    /// qui l'amène vers sa destination sans raccord visible.
    /// </summary>
    private void Release((double X, double Y) velocity)
    {
        DisplayInfo display = DetachDisplay();
        ScreenRect work = WorkAreaDip(display);
        ScreenRect pill = NominalPillAt(_pillSpring.X, _pillSpring.Y);

        FloatingTarget target = Detachment.Land(
            pill,
            velocity.X,
            velocity.Y,
            work,
            AttachCenterX(display),
            _settings.Landing);

        _landing = target;

        (double tx, double ty) = target.Landing == FloatingLanding.Reattach
            ? DockCenter(target.Edge, target.Offset)
            : (target.X + (pill.Width / 2), target.Y + (pill.Height / 2));

        _pillSpring.SetParameters(ThrowSpring);
        _pillSpring.SetTarget(tx, ty);

        if (!UseSpringAnimations())
        {
            _pillSpring.Snap(tx, ty);
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
        ScreenRect screen = ScreenDip(display);
        bool animate = AnimateGoo;

        // La forme accrochée, telle qu'elle est dessinée : c'est la goutte.
        (ScreenRect attachedRect, IslandFootprint drawn) = AttachedRect(_edge, _controller.CurrentFootprint, display, pull: 0);
        StartGoo(_edge, attachedRect, drawn, screen, rising: false);

        IslandFootprint local = LocalOf(_edge, drawn);
        double pulledDepth = Detachment.Pulled(local, _pull).Height;

        // La pastille a la forme de repos du contenu — pas celle de la
        // languette qu'elle quitte.
        IslandFootprint body = Detachment.FloatingOf(
            FitRestFor(_controller.PresentedActivity, _tier),
            _settings.Geometry.Shoulder);

        // Elle naît au bout de la forme étirée, là où la matière était, et la
        // prise est conservée : elle reste sous la main à l'endroit saisi.
        (double startX, double startY) = _edge switch
        {
            NotchEdge.Left => (pulledDepth - (body.Width / 2), attachedRect.CenterY),
            NotchEdge.Right => (screen.Right - pulledDepth + (body.Width / 2), attachedRect.CenterY),
            _ => (attachedRect.CenterX, pulledDepth - (body.Height / 2))
        };

        (_grabX, _grabY) = _edge switch
        {
            NotchEdge.Left => (_pressX - (body.Width / 2), _pressY - attachedRect.CenterY),
            NotchEdge.Right => (_pressX - (screen.Right - (body.Width / 2)), _pressY - attachedRect.CenterY),
            _ => (_pressX - attachedRect.CenterX, _pressY - (body.Height / 2))
        };

        _pillSpring.SetParameters(_settings.DetachFollowSpring);
        _pillSpring.Snap(startX, startY);

        _pop.Snap(UseSpringAnimations() ? PopFrom : 1);
        _pop.SetTarget(1);

        _goo = animate ? 0 : 1;
        _gooRate = animate ? 1 / GooBridge.TearSeconds : 0;

        _dragPhase = DragPhase.Dragging;
        _pull = 0;

        _previewEnterTimer?.Stop();
        _hoverExpandTimer?.Stop();
        _controller.EndPreview();

        // La notch devient pastille : disposition flottante d'abord, puis la
        // forme de repos du contenu, posée sans ressort — la goutte fait la
        // transition.
        _attachment = NotchAttachment.Floating;
        ApplyLayout();
        RefreshRestFootprint();
        RequestRender();
        MiniLog($"détachée du bord {_edge}");
    }

    /// <summary>
    /// Prépare la goutte pour un bord : la forme accrochée d'où elle part — ou
    /// qu'elle reforme —, sa position le long du bord, et la taille qu'a la
    /// pastille quand elle est fondue dedans.
    /// </summary>
    private void StartGoo(NotchEdge edge, ScreenRect attachedRect, IslandFootprint drawn, ScreenRect screen, bool rising)
    {
        double shoulder = ShoulderFor(edge);
        IslandFootprint local = LocalOf(edge, drawn);
        double s = IslandShape.EffectiveShoulder(local.Width, local.Height, shoulder);

        _gooEdge = edge;
        _gooAttached = local;
        _gooShoulder = shoulder;
        _gooCenterU = EdgeFrame.ToLocal(edge, attachedRect, screen).CenterX;

        // La pastille fondue dans la forme accrochée en a le corps, sans les
        // épaules : à la fin d'un raccrochage elle y disparaît exactement.
        _gooOrigin = EdgeFrame.IsSide(edge)
            ? new IslandFootprint(drawn.Width, Math.Max(1, drawn.Height - (2 * s)))
            : new IslandFootprint(Math.Max(1, drawn.Width - (2 * s)), drawn.Height);

        if (rising)
        {
            _goo = 1;
            _gooRate = -1 / GooBridge.ReattachSeconds;
        }
    }

    /// <summary>Reprise d'une pastille déjà flottante.</summary>
    private void BeginFloatingDrag()
    {
        _pendingClickTimer?.Stop();

        _grabX = _pressX - _pillSpring.X;
        _grabY = _pressY - _pillSpring.Y;

        _pillSpring.SetParameters(_settings.DetachFollowSpring);
        _dragPhase = DragPhase.Dragging;

        _previewEnterTimer?.Stop();
        _hoverExpandTimer?.Stop();
        _controller.EndPreview();
    }

    /// <summary>
    /// Cible du ressort pendant que la main tient la pastille : sous la main, à
    /// la prise près, avec une résistance élastique au-delà des bords de
    /// l'écran — la pastille ne sort pas, elle rechigne ; poussée assez loin
    /// vers un écran voisin, elle y passe (voir <see cref="CrossMonitorIfNeeded"/>).
    /// </summary>
    private void FollowPointer(double x, double y)
    {
        DisplayInfo display = DetachDisplay();
        ScreenRect screen = ScreenDip(display);
        ScreenRect work = WorkAreaDip(display);
        ScreenRect pill = NominalPillAt(0, 0);

        double halfWidth = pill.Width / 2;
        double halfHeight = pill.Height / 2;

        // Les côtés et le haut restent atteignables même sous une barre des
        // tâches : c'est là que la notch se raccroche.
        double targetX = Resist(x - _grabX, screen.X + halfWidth, screen.Right - halfWidth, pill.Width);
        double targetY = Resist(y - _grabY, screen.Y + halfHeight, work.Bottom - halfHeight, pill.Height);

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
    /// La main est-elle entrée dans un autre écran ? Assez loin — ou dès
    /// l'entrée si la résistance est désactivée —, la pastille y passe : son
    /// repère devient celui du nouvel écran, à la même place physique, et sa
    /// vitesse est convertie à la nouvelle échelle.
    /// </summary>
    private void CrossMonitorIfNeeded()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            return;
        }

        DisplayInfo current = DetachDisplay();
        DisplayInfo under = DisplayManager.GetDisplayForPoint(cursor.X, cursor.Y);

        if (under.Handle == current.Handle)
        {
            return;
        }

        double beyondX = Math.Max(Math.Max(current.Left - cursor.X, cursor.X - current.Right), 0);
        double beyondY = Math.Max(Math.Max(current.Top - cursor.Y, cursor.Y - current.Bottom), 0);
        double penetration = Math.Max(beyondX, beyondY) / current.DpiScale;

        if (!Detachment.CrossesMonitor(penetration, _settings.MonitorResistance))
        {
            return;
        }

        double from = current.DpiScale;
        double to = under.DpiScale;

        (double X, double Y) Convert(double x, double y)
            => ((current.Left + (x * from) - under.Left) / to, (current.Top + (y * from) - under.Top) / to);

        (double px, double py) = Convert(_pillSpring.X, _pillSpring.Y);
        (double vx, double vy) = (_pillSpring.VelocityX * from / to, _pillSpring.VelocityY * from / to);

        _pillSpring.Snap(px, py);
        _pillSpring.SetVelocity(vx, vy);

        (_pressX, _pressY) = Convert(_pressX, _pressY);
        _grabX *= from / to;
        _grabY *= from / to;

        _velocity.Reset();
        _detachDisplay = under;
        _shape.Forget();

        MiniLog("changement d'écran");
    }

    /// <summary>
    /// La pastille est arrivée. Sur un aimant ou à l'endroit où elle a été
    /// posée, elle y reste ; à un bord, elle se raccroche.
    /// </summary>
    private void SettleLanding(bool immediate)
    {
        if (_landing.Landing == FloatingLanding.Reattach)
        {
            if (immediate)
            {
                FinishReattach(_landing.Edge, _landing.Offset);
            }

            return;
        }

        _pill = NominalPillAt(_pillSpring.X, _pillSpring.Y);
        _dragPhase = DragPhase.None;
        _presence.Recheck();
        ApplyGeometry(_controller.CurrentFootprint);
    }

    /// <summary>
    /// La notch retrouve un bord : la goutte a fini de l'aspirer, elle est de
    /// nouveau accrochée — en haut, ou en languette sur un côté. Le bord, sa
    /// position et l'écran sont enregistrés : c'est là qu'elle redémarrera.
    /// </summary>
    private void FinishReattach(NotchEdge edge, double offset)
    {
        DisplayInfo display = DetachDisplay();

        _edge = _settings.AllowSideEdges ? edge : NotchEdge.Top;
        _dockOffset = Math.Clamp(offset, 0, 1);
        _attachment = NotchAttachment.Attached;
        _dragPhase = DragPhase.None;
        _landing = new FloatingTarget(FloatingLanding.Stay, 0, 0);
        _goo = 1;
        _gooRate = 0;
        _pop.Snap(1);
        _pendingClickTimer?.Stop();

        ApplyLayout();
        RefreshRestFootprint();
        RequestRender();

        PersistDock(display);
        _detachDisplay = null;

        ApplyGeometry(_controller.CurrentFootprint);
        MiniLog($"raccrochée au bord {_edge}");
    }

    /// <summary>
    /// Enregistre le bord, la position et l'écran d'accroche, s'ils ont
    /// changé. L'écran est repéré par son rectangle : un écran débranché ramène
    /// la notch sur l'écran principal au prochain démarrage.
    /// </summary>
    private void PersistDock(DisplayInfo display)
    {
        string bounds = DisplayBoundsOf(display);
        bool sameDisplay = string.Equals(bounds, _settings.DockDisplayBounds, StringComparison.Ordinal)
            && _settings.DisplayMode == IslandDisplayMode.Custom;

        if (sameDisplay
            && _settings.DockEdge == _edge
            && Math.Abs(_settings.DockOffset - _dockOffset) < 0.001)
        {
            return;
        }

        _settingsService.Update(s =>
        {
            s.DockEdge = _edge;
            s.DockOffset = _dockOffset;
            s.DockDisplayBounds = bounds;
            s.DisplayMode = IslandDisplayMode.Custom;
        });
    }

    /// <summary>Rectangle physique d'un écran, « gauche,haut,largeur,hauteur ».</summary>
    private static string DisplayBoundsOf(DisplayInfo display)
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{display.Left},{display.Top},{display.Width},{display.Height}");

    /// <summary>Dernière recherche d'écran par rectangle : oubliée à chaque changement d'écrans.</summary>
    private (string? Bounds, DisplayInfo? Display)? _dockDisplayCache;

    /// <summary>
    /// Écran enregistré par son rectangle, s'il est toujours branché. La
    /// géométrie le demande à chaque image d'un ressort : la réponse est
    /// gardée jusqu'au prochain changement d'écrans.
    /// </summary>
    private DisplayInfo? FindDisplayByBounds(string? bounds)
    {
        if (string.IsNullOrWhiteSpace(bounds))
        {
            return null;
        }

        if (_dockDisplayCache is { } cached && string.Equals(cached.Bounds, bounds, StringComparison.Ordinal))
        {
            return cached.Display;
        }

        DisplayInfo? found = null;

        foreach (DisplayInfo display in DisplayManager.GetAllDisplays())
        {
            if (string.Equals(DisplayBoundsOf(display), bounds, StringComparison.Ordinal))
            {
                found = display;
                break;
            }
        }

        _dockDisplayCache = (bounds, found);
        return found;
    }

    /// <summary>Double-clic sur la pastille : retour au dernier bord, sur l'écran où elle se trouve.</summary>
    private void ReattachToDock() => ReattachTo(_edge, _dockOffset);

    /// <summary>
    /// Raccroche la notch à un bord : elle vole jusqu'à lui et la goutte
    /// l'aspire, exactement comme après un lancer.
    /// </summary>
    private void ReattachTo(NotchEdge edge, double offset)
    {
        if (!UsesFloatingGeometry || _pointerDown)
        {
            return;
        }

        edge = _settings.AllowSideEdges ? edge : NotchEdge.Top;

        if (!UseSpringAnimations())
        {
            FinishReattach(edge, offset);
            return;
        }

        if (_dragPhase == DragPhase.None)
        {
            ScreenRect rest = CurrentPillRect();
            _pillSpring.Snap(rest.CenterX, rest.CenterY);
        }

        _controller.RequestCollapse();

        (double tx, double ty) = DockCenter(edge, offset);
        _landing = new FloatingTarget(FloatingLanding.Reattach, 0, 0, edge, offset);
        _pillSpring.SetParameters(ThrowSpring);
        _pillSpring.SetTarget(tx, ty);
        _dragPhase = DragPhase.Settling;

        HookDetachFrames();
    }

    /// <summary>Menu : raccroche au dernier bord.</summary>
    private void ReattachFromMenu() => ReattachToDock();

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

        if (UsesFloatingGeometry)
        {
            FinishReattach(_edge, _dockOffset);
        }
        else
        {
            _dragPhase = DragPhase.None;
            _detachDisplay = null;
            ApplyGeometry(_controller.CurrentFootprint);
        }

        HookDetachFrames();
    }

    /// <summary>
    /// Centre visé par la pastille qui se raccroche : le corps de la forme
    /// accrochée au repos, là où elle va se fondre.
    /// </summary>
    private (double X, double Y) DockCenter(NotchEdge edge, double offset)
    {
        DisplayInfo display = DetachDisplay();
        IslandFootprint rest = RestFootprintFor(edge);
        double saved = _dockOffset;

        _dockOffset = offset;
        (ScreenRect rect, _) = AttachedRect(edge, rest, display, pull: 0);
        _dockOffset = saved;

        return (rect.CenterX, rect.CenterY);
    }

    /// <summary>Forme de repos d'un bord, au repos : la notch du haut ou la languette.</summary>
    private IslandFootprint RestFootprintFor(NotchEdge edge)
        => EdgeFrame.IsSide(edge)
            ? SideTab.Rest(_settings.TabSize, _settings.SideShoulderRadius)
            : FitRestFor(_controller.PresentedActivity, _tier);

    /// <summary>Recalcule la forme de repos pour l'état courant et la pose sans ressort.</summary>
    private void RefreshRestFootprint()
    {
        _restFootprint = FitRest(_controller.PresentedActivity, _tier);
        _controller.UpdateCollapsedFootprint(_restFootprint);
        _controller.SnapToRest();
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
            || _bubbleGooRate != 0
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

        if (_dragPhase == DragPhase.Dragging)
        {
            CrossMonitorIfNeeded();
        }

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
                    // puis revient. Elle ne cède simplement jamais.
                    _dragPhase = DragPhase.Pulling;
                    _previewEnterTimer?.Stop();
                    _hoverExpandTimer?.Stop();
                }

                break;

            case DragPhase.Pulling:
                _pull = PullOf((x - _pressX, y - _pressY));

                if (_settings.AllowDetach && Detachment.ShouldTear(_pull, _settings.TearDistance))
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

        StepBubbleGoo(dt);

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

        if (_landing.Landing != FloatingLanding.Reattach)
        {
            if (_pillSpring.IsSettled)
            {
                SettleLanding(immediate: false);
            }

            return;
        }

        // Raccrochage : quand la pastille arrive au bord, la goutte l'aspire —
        // la même fonction qu'à l'arrachement, jouée à l'envers.
        DisplayInfo display = DetachDisplay();
        ScreenRect screen = ScreenDip(display);
        NotchEdge edge = _landing.Edge;

        if (_gooRate == 0 && _goo >= 1)
        {
            double saved = _dockOffset;
            _dockOffset = _landing.Offset;
            (ScreenRect rect, IslandFootprint drawn) = AttachedRect(edge, RestFootprintFor(edge), display, pull: 0);
            _dockOffset = saved;

            ScreenRect pillLocal = EdgeFrame.ToLocal(edge, NominalPillAt(_pillSpring.X, _pillSpring.Y), screen);

            if (pillLocal.Y < LocalOf(edge, drawn).Height)
            {
                if (AnimateGoo)
                {
                    StartGoo(edge, rect, drawn, screen, rising: true);
                }
                else
                {
                    _goo = 0;
                }
            }
        }

        bool arrived = Math.Abs(_pillSpring.X - _pillSpring.TargetX) < 1.5
            && Math.Abs(_pillSpring.Y - _pillSpring.TargetY) < 1.5;

        if (_goo <= 0 && arrived)
        {
            FinishReattach(edge, _landing.Offset);
        }
    }

    // ------------------------------------------------------------------
    // Géométrie accrochée à un côté
    // ------------------------------------------------------------------

    /// <summary>
    /// Rectangle et encombrement dessiné d'une forme accrochée à un bord, pour
    /// un encombrement exprimé comme en haut. Le tirage l'allonge vers
    /// l'intérieur.
    /// </summary>
    private (ScreenRect Rect, IslandFootprint Drawn) AttachedRect(
        NotchEdge edge,
        IslandFootprint footprint,
        DisplayInfo display,
        double pull)
    {
        ScreenRect screen = ScreenDip(display);

        if (!EdgeFrame.IsSide(edge))
        {
            IslandFootprint top = pull > 0 ? Detachment.Pulled(footprint, pull) : footprint;
            return (new ScreenRect(AttachCenterX(display) - (top.Width / 2), 0, top.Width, top.Height), top);
        }

        IslandFootprint drawn = EdgeFrame.Oriented(footprint, edge, _settings.SideShoulderRadius);

        if (pull > 0)
        {
            IslandFootprint pulled = Detachment.Pulled(LocalOf(edge, drawn), pull);
            drawn = new IslandFootprint(pulled.Height, pulled.Width);
        }

        return (SideTab.Place(edge, drawn, _dockOffset, screen, WorkAreaDip(display)), drawn);
    }

    /// <summary>Encombrement dans le repère du bord : longueur le long du bord, profondeur vers l'intérieur.</summary>
    private static IslandFootprint LocalOf(NotchEdge edge, IslandFootprint drawn)
        => EdgeFrame.IsSide(edge) ? new IslandFootprint(drawn.Height, drawn.Width) : drawn;

    /// <summary>
    /// Pose la fenêtre pour une notch accrochée à un côté : la languette au
    /// repos, ou la forme ouverte qui s'avance vers le centre de l'écran.
    /// </summary>
    private void ApplySideGeometry(IslandFootprint footprint)
    {
        DisplayInfo display = ResolveDisplay();
        double scale = display.DpiScale;
        double pull = _dragPhase is DragPhase.Pulling or DragPhase.Unpulling ? _pull : 0;

        (ScreenRect rect, IslandFootprint drawn) = AttachedRect(_edge, footprint, display, pull);

        int widthPx = MonitorDpi.ToPhysicalPixels(drawn.Width, scale);
        int heightPx = MonitorDpi.ToPhysicalPixels(drawn.Height, scale);
        int x = _edge == NotchEdge.Right ? display.Right - widthPx : display.Left;
        int y = display.Top + (int)Math.Round(rect.Y * scale);

        MoveWindow(x, y, widthPx, heightPx, recheck: true);

        IslandBody.Width = drawn.Width;
        IslandBody.Height = drawn.Height;

        double shoulder = _settings.SideShoulderRadius;
        ShapePoint[] outline = EdgeFrame.Silhouette(_settings.Geometry, drawn, _edge, shoulder);

        if (IslandGeometryFactory.FromPolygons([outline], 0, 0) is { } silhouette)
        {
            SurfaceFill.Data = silhouette;
        }

        RememberOutline(() => outline);

        // Les épaules sont en haut et en bas de la languette : le contenu se
        // mesure entre elles.
        IslandFootprint local = LocalOf(_edge, drawn);
        double s = IslandShape.EffectiveShoulder(local.Width, local.Height, shoulder);

        if (Math.Abs(s - _contentShoulder) > 0.25)
        {
            _contentShoulder = s;
            ContentArea.Margin = new Thickness(0, s, 0, s);
        }

        double radius = IslandShape.EffectiveRadius(local.Width, local.Height, _settings.Geometry.RadiusFor(local), s);

        _atmosphere?.PositionAround(new AtmospherePlacement(
            x,
            y,
            widthPx,
            heightPx,
            drawn.Width,
            drawn.Height,
            radius,
            s,
            0,
            Edge: _edge));

        PlaceBubbleAttached(display, rect, drawn);
    }

    // ------------------------------------------------------------------
    // Géométrie flottante
    // ------------------------------------------------------------------

    /// <summary>
    /// Pose la fenêtre pour une notch détachée : la pastille, et tant qu'elle
    /// existe la goutte qui la relie à son bord. La fenêtre couvre exactement
    /// ce qui est dessiné.
    /// </summary>
    private void ApplyFloatingGeometry(IslandFootprint footprint)
    {
        DisplayInfo display = DetachDisplay();
        double scale = display.DpiScale;
        NotchGeometry geometry = _settings.Geometry;
        ScreenRect screen = ScreenDip(display);
        ScreenRect work = WorkAreaDip(display);

        IslandFootprint body = Detachment.FloatingOf(footprint, geometry.Shoulder);
        bool moving = _dragPhase is DragPhase.Dragging or DragPhase.Settling || !_pop.IsSettled || GooActive;

        ScreenRect pill;

        if (moving)
        {
            (double sx, double sy) = UseSpringAnimations()
                ? FluidMotion.Stretch(_pillSpring.VelocityX, _pillSpring.VelocityY, _settings.StretchAmount)
                : (1, 1);

            // Fondue dans la goutte, la pastille a la taille du corps accroché ;
            // elle prend la sienne à mesure que la goutte la libère.
            IslandFootprint size = body;

            if (GooActive)
            {
                double k = SmoothStep(_goo);
                size = new IslandFootprint(
                    _gooOrigin.Width + ((body.Width - _gooOrigin.Width) * k),
                    _gooOrigin.Height + ((body.Height - _gooOrigin.Height) * k));
            }

            double pop = _pop.Value;
            pill = ScreenRect.Centered(_pillSpring.X, _pillSpring.Y, size.Width * sx * pop, size.Height * sy * pop);
        }
        else
        {
            pill = Detachment.Anchor(_pill, body, work);
        }

        // La trace et le fil de la goutte, calculés dans le repère de son bord.
        List<ShapePoint[]>? goo = null;
        ScreenRect bounds = pill;

        if (GooActive)
        {
            IslandFootprint residue = GooBridge.Residue(_gooAttached, _goo);
            var residueLocal = new ScreenRect(_gooCenterU - (residue.Width / 2), 0, residue.Width, residue.Height);
            ScreenRect pillLocal = EdgeFrame.ToLocal(_gooEdge, pill, screen);

            goo = [];

            if (residue.Height > 0.5)
            {
                ShapePoint[] local = (geometry with { Shoulder = _gooShoulder }).Silhouette(residue);
                goo.Add(EdgeFrame.ToScreen(_gooEdge, local, residueLocal.X, screen));
                bounds = Union(bounds, EdgeFrame.ToScreenRect(_gooEdge, residueLocal, screen));
            }

            foreach (ShapePoint[] piece in GooBridge.Neck(residueLocal, pillLocal, _goo))
            {
                goo.Add(EdgeFrame.ToScreen(_gooEdge, piece, 0, screen));
            }
        }

        int winX = display.Left + (int)Math.Floor(bounds.X * scale);
        int winY = display.Top + (int)Math.Floor(bounds.Y * scale);
        int winRight = display.Left + (int)Math.Ceiling(bounds.Right * scale);
        int winBottom = display.Top + (int)Math.Ceiling(bounds.Bottom * scale);

        MoveWindow(winX, winY, Math.Max(1, winRight - winX), Math.Max(1, winBottom - winY), recheck: !moving);

        // L'origine du contenu, en DIPs d'écran : c'est elle qui aligne au
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

        var drawnPill = new IslandFootprint(pill.Width, pill.Height);
        NotchGeometry floating = geometry with { ExpandedRadius = _settings.FloatingRadius };
        double radius = floating.FloatingRadiusFor(drawnPill);

        double smoothing = floating.FloatingSmoothingFor(drawnPill);

        if (_shape.Build(drawnPill, radius, smoothing, floating: true) is { } silhouette)
        {
            SurfaceFill.Data = silhouette;
        }

        RememberOutline(() => IslandShape.Floating(drawnPill.Width, drawnPill.Height, radius, smoothing));

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

    private static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - (2 * x));
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

    /// <summary>Mode de disposition appliqué en dernier : il n'est changé qu'au passage d'un mode à l'autre.</summary>
    private (NotchAttachment Attachment, NotchEdge Edge)? _layout;

    /// <summary>
    /// Dispose le corps de la notch selon sa nature : accrochée en haut,
    /// accrochée à un côté, ou flottante. Les marges d'épaules, l'alignement et
    /// le reflet en dépendent ; ils ne sont touchés qu'au changement.
    /// </summary>
    private void ApplyLayout()
    {
        var layout = (_attachment, _edge);

        if (_layout == layout)
        {
            return;
        }

        _layout = layout;
        _shape.Forget();
        _contentShoulder = double.NaN;

        if (UsesFloatingGeometry)
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
            GooFill.Stroke = SurfaceFill.Stroke;
            GooFill.StrokeThickness = SurfaceFill.StrokeThickness;
            return;
        }

        IslandBody.HorizontalAlignment = _edge switch
        {
            NotchEdge.Left => HorizontalAlignment.Left,
            NotchEdge.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };
        IslandBody.VerticalAlignment = VerticalAlignment.Top;
        IslandBody.Margin = new Thickness(0);

        ContentArea.Width = double.NaN;
        ContentArea.Height = double.NaN;
        ContentArea.HorizontalAlignment = HorizontalAlignment.Stretch;
        ContentArea.VerticalAlignment = VerticalAlignment.Stretch;

        // Le reflet rasant naît du bord du haut ; sur un côté, il n'a pas de sens.
        SpecularFill.Visibility = EdgeFrame.IsSide(_edge) ? Visibility.Collapsed : Visibility.Visible;
        GooFill.Visibility = Visibility.Collapsed;
        GooFill.Data = null;
    }

    // ------------------------------------------------------------------
    // Repères
    // ------------------------------------------------------------------

    private DisplayInfo DetachDisplay() => _detachDisplay ??= ResolveDisplay();

    /// <summary>Position du pointeur en DIPs, relative à l'écran figé.</summary>
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

    /// <summary>L'écran entier, en DIPs relatifs à son coin supérieur gauche.</summary>
    private static ScreenRect ScreenDip(DisplayInfo display)
        => new(0, 0, display.Width / display.DpiScale, display.Height / display.DpiScale);

    /// <summary>Zone de travail de l'écran, en DIPs relatifs à son coin supérieur gauche.</summary>
    private static ScreenRect WorkAreaDip(DisplayInfo display)
    {
        double scale = display.DpiScale;

        return new ScreenRect(
            (display.WorkLeft - display.Left) / scale,
            (display.WorkTop - display.Top) / scale,
            display.WorkWidth / scale,
            display.WorkHeight / scale);
    }

    /// <summary>Abscisse du centre de la notch accrochée en haut, en DIPs relatifs à l'écran.</summary>
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
