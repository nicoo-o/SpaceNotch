using System;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch_App.Composition;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Transitions entre les états de la notch, sur une seule ligne de temps. Voir
/// ADR-021.
///
/// <para>
/// <b>Le contenu arrive flou.</b> Quand un contenu en remplace un autre — une
/// activité qui change, la notch qui s'ouvre ou se referme —, il se lit d'abord
/// flou, puis se précise, comme sur la Dynamic Island. WinUI n'a pas de filtre
/// de flou sur un élément : le flou est un voile acrylique, tracé exactement
/// dans la silhouette, qui se dissipe. Le compositeur joue la dissipation ;
/// hors transition, le voile est retiré de l'arbre.
/// </para>
///
/// <para>
/// <b>La forme respire.</b> Quand une activité en remplace une autre, la notch
/// se resserre un instant puis s'élargit vers sa nouvelle forme : le changement
/// se voit comme un souffle, pas comme un saut de largeur.
/// </para>
/// </summary>
public sealed partial class IslandWindow
{
    /// <summary>Durée de dissipation du voile, en millisecondes.</summary>
    private const double VeilMilliseconds = 260;

    /// <summary>Temps du creux de la respiration avant que la forme ne s'élargisse.</summary>
    private static readonly TimeSpan BreathHold = TimeSpan.FromMilliseconds(110);

    /// <summary>Contour de la forme dessinée, dans le repère du corps : le voile le reprend.</summary>
    private Func<ShapePoint[]>? _veilOutline;

    /// <summary>Numéro de la dernière dissipation lancée : une plus ancienne ne retire pas le voile.</summary>
    private int _veilGeneration;

    private DispatcherQueueTimer? _breathTimer;

    /// <summary>Activité présentée au rendu précédent, pour reconnaître un changement.</summary>
    private string? _lastPresentedId;

    /// <summary>État au rendu précédent, pour reconnaître l'entrée dans l'aperçu ou la fermeture.</summary>
    private SpaceNotch.Core.State.IslandState _lastRenderedState;

    /// <summary>Crée le pinceau du voile : le flou natif, sans teinte, sur la teinte de la surface.</summary>
    private void ApplyVeilBrush()
    {
        try
        {
            VeilFill.Fill = new AcrylicBrush
            {
                TintColor = Colors.Black,
                TintOpacity = 0,
                TintLuminosityOpacity = 0,
                FallbackColor = Colors.Transparent
            };
        }
        catch (Exception)
        {
            // Sans acrylique, pas de flou : le contenu arrive par son seul fondu.
            VeilFill.Fill = null;
        }
    }

    /// <summary>
    /// Mémorise le contour de la forme qui vient d'être tracée ; si le voile est
    /// visible, il suit la forme image par image.
    /// </summary>
    private void RememberOutline(Func<ShapePoint[]> outline)
    {
        _veilOutline = outline;

        if (VeilFill.Visibility == Visibility.Visible)
        {
            VeilFill.Data = IslandGeometryFactory.FromPolygons([outline()], 0, 0);
        }
    }

    /// <summary>
    /// Le contenu vient de changer : il se lit flou, puis se précise. Rien sous
    /// réduction des animations, ni sans matière acrylique.
    /// </summary>
    /// <param name="delay">Attente avant la dissipation : le voile couvre d'abord le changement.</param>
    private void PlayVeil(TimeSpan delay = default)
    {
        if (!UseSpringAnimations() || VeilFill.Fill is null || _veilOutline is null)
        {
            return;
        }

        int generation = ++_veilGeneration;

        VeilFill.Data = IslandGeometryFactory.FromPolygons([_veilOutline()], 0, 0);
        VeilFill.Visibility = Visibility.Visible;

        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(VeilFill);
            Compositor compositor = visual.Compositor;

            ScalarKeyFrameAnimation clear = compositor.CreateScalarKeyFrameAnimation();
            clear.InsertKeyFrame(0f, 1f);
            clear.InsertKeyFrame(1f, 0f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.3f, 0f), new Vector2(0.2f, 1f)));
            clear.Duration = TimeSpan.FromMilliseconds(VeilMilliseconds);
            clear.DelayTime = delay;
            clear.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Opacity", clear);
            batch.End();
            batch.Completed += (_, _) => _dispatcherQueue.TryEnqueue(() =>
            {
                // Une dissipation plus récente garde le voile : c'est elle qui le retirera.
                if (generation == _veilGeneration)
                {
                    VeilFill.Visibility = Visibility.Collapsed;
                    VeilFill.Data = null;
                }
            });
        }
        catch (Exception)
        {
            VeilFill.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Respiration au changement d'activité : la forme se resserre, le contenu
    /// change sous le voile, puis elle s'élargit vers sa nouvelle forme.
    /// </summary>
    private void Breathe()
    {
        if (!UseSpringAnimations() || UsesSideTab)
        {
            return;
        }

        _controller.Pinch();

        _breathTimer ??= CreateOneShotTimer(BreathHold, _controller.Unpinch);
        _breathTimer.Stop();
        _breathTimer.Start();
    }

    /// <summary>
    /// Le contenu suit la forme qui grandit : il part de quelques DIPs plus
    /// haut — ou plus au bord sur un côté — et rejoint sa place.
    /// </summary>
    private void SlideWithGrowth(UIElement element)
    {
        if (!UseSpringAnimations())
        {
            return;
        }

        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            Compositor compositor = visual.Compositor;
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);

            Vector3 from = _edge switch
            {
                SpaceNotch.Core.Presentation.NotchEdge.Left when !UsesFloatingGeometry => new Vector3(-3, 0, 0),
                SpaceNotch.Core.Presentation.NotchEdge.Right when !UsesFloatingGeometry => new Vector3(3, 0, 0),
                _ => new Vector3(0, -3, 0)
            };

            Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0f, from);
            slide.InsertKeyFrame(1f, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0f, 1f)));
            slide.Duration = TimeSpan.FromMilliseconds(MotionPresets.DurationMs(MotionKind.Standard));

            visual.StartAnimation("Translation", slide);
        }
        catch (Exception)
        {
            // Une transition refusée laisse le contenu à sa place.
        }
    }

    /// <summary>Vue de repos visible en ce moment, s'il y en a une.</summary>
    private FrameworkElement? VisibleRestView()
    {
        if (TabRestView.Visibility == Visibility.Visible)
        {
            return TabRestView;
        }

        if (CardRestView.Visibility == Visibility.Visible)
        {
            return CardRestView;
        }

        return SignalRestView.Visibility == Visibility.Visible ? SignalRestView : null;
    }
}
