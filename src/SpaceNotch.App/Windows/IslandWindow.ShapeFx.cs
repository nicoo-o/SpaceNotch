using System;
using Microsoft.UI.Dispatching;
using SpaceNotch.Core.Scenes;
using SpaceNotch_App.Composition;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Déformations passagères de la silhouette accrochée en haut :
/// la goutte qui pend vers un fichier et l'onde du dépôt (P2), les coins en
/// escalier d'une erreur (A8). Chacune ne vit que quelques centaines de
/// millisecondes ; hors de ces instants, la silhouette ordinaire est tracée.
/// </summary>
public sealed partial class IslandWindow
{
    private static readonly TimeSpan FxFrame = TimeSpan.FromMilliseconds(16);

    /// <summary>Durée de l'escalier des coins, en millisecondes : 220 de bascule, puis le temps de lire.</summary>
    private const int CrenelMilliseconds = 1400;

    /// <summary>Durée de l'onde du dépôt, en secondes.</summary>
    private const double RippleSeconds = 0.9;

    private DispatcherQueueTimer? _fxTimer;
    private double _dropTarget;
    private double _drop;
    private double _dropX = double.NaN;
    private double _ripple;
    private double _ripplePhase;
    private DateTime _crenelUntil;

    /// <summary>Vrai tant qu'une déformation est à tracer.</summary>
    private bool ShapeFxActive => _drop > 0.01 || _dropTarget > 0 || _ripple > 0.05 || DateTime.UtcNow < _crenelUntil;

    /// <summary>Un fichier survole la notch à l'abscisse <paramref name="x"/> (DIP, repère de la notch) : la goutte pend vers lui.</summary>
    private void HangDrop(double x)
    {
        if (!UseSpringAnimations() || UsesFloatingGeometry || UsesSideTab)
        {
            return;
        }

        if (_dropTarget <= 0)
        {
            SpaceNotch.Infrastructure.Logging.MiniLogger.Log("[FX] goutte vers un fichier");
        }

        _dropX = double.IsNaN(_dropX) ? x : _dropX;
        _dropTarget = 1;
        _pendingDropX = x;
        StartFx();
    }

    private double _pendingDropX;

    /// <summary>Le fichier part sans être déposé : la goutte remonte.</summary>
    private void RetractDrop()
    {
        _dropTarget = 0;
        StartFx();
    }

    /// <summary>Le fichier est bu : la goutte remonte d'un coup et le bord ondule.</summary>
    private void SwallowDrop()
    {
        if (!UseSpringAnimations() || UsesFloatingGeometry || UsesSideTab)
        {
            return;
        }

        _dropTarget = 0;
        _ripple = ShapeEffects.DropDepth * 0.5;
        _ripplePhase = 0;
        StartFx();
    }

    /// <summary>Une erreur : les coins passent en escalier, puis redeviennent lisses (A8).</summary>
    private void CrenelCorners()
    {
        if (!UseSpringAnimations() || UsesFloatingGeometry || UsesSideTab)
        {
            return;
        }

        _crenelUntil = DateTime.UtcNow.AddMilliseconds(CrenelMilliseconds);
        StartFx();
    }

    private string? _crenelledId;

    /// <summary>
    /// Une activité en échec change la forme avant le texte (A8) — une seule
    /// fois par échec, pas à chaque rendu.
    /// </summary>
    private void CrenelOnError(SpaceNotch.Core.Activities.IslandActivity? activity)
    {
        if (activity?.MotionState != SpaceNotch.Core.Motion.ActivityMotionState.Error)
        {
            _crenelledId = null;
            return;
        }

        if (string.Equals(_crenelledId, activity.Id, StringComparison.Ordinal))
        {
            return;
        }

        _crenelledId = activity.Id;
        CrenelCorners();
    }

    private void StartFx()
    {
        if (_fxTimer is null)
        {
            _fxTimer = TrackTimer(DispatcherQueue.CreateTimer());
            _fxTimer.Interval = FxFrame;
            _fxTimer.IsRepeating = true;
            _fxTimer.Tick += (_, _) => FxTick();
        }

        _fxTimer.Start();
    }

    private void FxTick()
    {
        double dt = FxFrame.TotalSeconds;

        // La goutte suit le pointeur à ressort doux et descend ou remonte
        // avec une constante de temps de 90 ms.
        _dropX += (_pendingDropX - _dropX) * Math.Min(1, dt / 0.08);
        _drop += (_dropTarget - _drop) * Math.Min(1, dt / 0.09);

        if (_ripple > 0)
        {
            _ripplePhase += dt * 2 * Math.PI * 3.2;
            _ripple *= Math.Exp(-dt / (RippleSeconds / 3));
        }

        bool active = ShapeFxActive;

        if (!active)
        {
            _fxTimer?.Stop();
            _drop = 0;
            _ripple = 0;
            _dropX = double.NaN;
            _shape.Forget();
        }

        ApplyShape(_controller.CurrentFootprint);
    }

    /// <summary>
    /// Silhouette déformée, ou <c>null</c> quand aucune déformation n'est en
    /// cours : l'appelant trace alors la silhouette ordinaire.
    /// </summary>
    private Microsoft.UI.Xaml.Media.Geometry? DeformedSilhouette(IslandFootprint footprint)
    {
        if (!ShapeFxActive || UsesFloatingGeometry || UsesSideTab)
        {
            return null;
        }

        NotchGeometry geometry = _settings.Geometry;
        ShapePoint[] outline;

        if (_drop > 0.01 || _ripple > 0.05)
        {
            var body = new IslandFootprint(footprint.Width, Math.Max(1, footprint.Height - ShapeEffects.DropDepth));
            outline = ShapeEffects.Liquid(geometry.Silhouette(body), body.Height, double.IsNaN(_dropX) ? footprint.Width / 2 : _dropX, _drop, _ripple, _ripplePhase);
        }
        else
        {
            outline = geometry.Silhouette(footprint);
        }

        if (DateTime.UtcNow < _crenelUntil)
        {
            outline = ShapeEffects.Crenellate(outline, footprint.Height, geometry.RadiusFor(footprint));
        }

        // La prochaine silhouette ordinaire devra se reconstruire.
        _shape.Forget();
        return IslandGeometryFactory.FromPolygons([outline], 0, 0);
    }
}
