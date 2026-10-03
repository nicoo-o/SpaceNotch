using System;
using Microsoft.UI.Dispatching;
using SpaceNotch.Core.Scenes;
using SpaceNotch_App.Composition;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Déformations passagères de la silhouette accrochée en haut :
/// la goutte qui pend vers un fichier et l'onde du dépôt (P2). Elle ne vit
/// que quelques centaines de millisecondes ; hors de ces instants, la
/// silhouette ordinaire est tracée.
/// </summary>
public sealed partial class IslandWindow
{
    /// <summary>Durée de l'onde du dépôt, en secondes.</summary>
    private const double RippleSeconds = 0.9;

    private double _dropTarget;
    private double _drop;
    private double _dropX = double.NaN;
    private double _ripple;
    private double _ripplePhase;

    /// <summary>Vrai tant qu'une déformation est à tracer.</summary>
    private bool ShapeFxActive => _drop > 0.01 || _dropTarget > 0 || _ripple > 0.05;

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

    private string? _shakenId;

    /// <summary>
    /// Une activité en échec : la silhouette reste lisse, la notch secoue
    /// brièvement (la secousse de la butée), une seule fois par échec. La
    /// teinte d'erreur de l'icône et du titre dit le reste.
    /// </summary>
    private void ShakeOnError(SpaceNotch.Core.Activities.IslandActivity? activity)
    {
        if (activity?.MotionState != SpaceNotch.Core.Motion.ActivityMotionState.Error)
        {
            _shakenId = null;
            return;
        }

        if (string.Equals(_shakenId, activity.Id, StringComparison.Ordinal))
        {
            return;
        }

        _shakenId = activity.Id;
        BumpContent();
    }

    /// <summary>
    /// La goutte et l'onde suivent l'horloge d'images unique (phase D) au lieu
    /// d'un minuteur à 16 ms qui doublait le signal d'image du ressort.
    /// </summary>
    private void StartFx()
    {
        if (_fxRunning)
        {
            return;
        }

        _fxRunning = true;
        _fxLast = System.Diagnostics.Stopwatch.GetTimestamp();
        SpaceNotch_App.Animations.FrameClock.Rendering += OnFxFrame;
    }

    private void StopFx()
    {
        if (!_fxRunning)
        {
            return;
        }

        _fxRunning = false;
        SpaceNotch_App.Animations.FrameClock.Rendering -= OnFxFrame;
    }

    private bool _fxRunning;
    private long _fxLast;

    private void OnFxFrame(object? sender, object e)
    {
        if (_isClosed)
        {
            StopFx();
            return;
        }

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double dt = Math.Clamp((now - _fxLast) / (double)System.Diagnostics.Stopwatch.Frequency, 0, 1.0 / 15);
        _fxLast = now;
        FxTick(dt);
    }

    private void FxTick(double dt)
    {

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
            StopFx();
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
        if (UsesFloatingGeometry || UsesSideTab)
        {
            return null;
        }

        if (!ShapeFxActive)
        {
            return BulgedSilhouette(footprint);
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

        // La prochaine silhouette ordinaire devra se reconstruire.
        _shape.Forget();
        return IslandGeometryFactory.FromPolygons([outline], 0, 0);
    }

    /// <summary>
    /// Physique B « Liquide doux » : pendant le ressort, le bas de la forme
    /// se bombe quand elle descend et se creuse un peu quand elle remonte,
    /// selon sa vitesse. <c>null</c> au repos ou sous un mouvement réduit.
    /// </summary>
    private Microsoft.UI.Xaml.Media.Geometry? BulgedSilhouette(IslandFootprint footprint)
    {
        // La forme est déjà posée pendant la construction de la fenêtre, avant
        // que le contrôleur existe.
        if (_controller is null || _dragPhase is not DragPhase.None || !UseSpringAnimations())
        {
            return null;
        }

        double bulge = SpaceNotch.Core.Motion.MotionPresets.Bulge(_controller.HeightVelocity);

        if (Math.Abs(bulge) < 0.1)
        {
            if (_bulged)
            {
                // Retour à la silhouette ordinaire : elle doit se reconstruire.
                _bulged = false;
                _shape.Forget();
            }

            return null;
        }

        NotchGeometry geometry = _settings.Geometry;
        double bodyHeight = ShapeEffects.BulgeBody(footprint.Height, bulge);
        var body = new IslandFootprint(footprint.Width, bodyHeight);
        ShapePoint[] outline = ShapeEffects.Bulged(geometry.Silhouette(body), bodyHeight, bulge);

        _bulged = true;
        _shape.Forget();
        return IslandGeometryFactory.FromPolygons([outline], 0, 0);
    }

    private bool _bulged;
}
