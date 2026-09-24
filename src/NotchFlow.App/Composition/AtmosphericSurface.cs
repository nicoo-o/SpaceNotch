using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace NotchFlow_App.Composition;

/// <summary>
/// Surface atmosphérique portée par le compositeur.
///
/// C'est la pièce qui matérialise la contrainte esthétique du projet : le corps
/// de l'Island ne doit présenter aucune arête — ni en bas, ni sur les côtés. Le
/// fondu est un <em>masque d'opacité</em> appliqué par le GPU à une teinte unie :
/// il suit la surface à chaque image, et rien n'est recalculé au repos.
///
/// <para>
/// <b>Pourquoi un masque capturé plutôt qu'un dégradé direct.</b>
/// <c>CompositionMaskBrush.Mask</c> n'accepte que deux types :
/// <c>CompositionSurfaceBrush</c> ou <c>CompositionNineGridBrush</c>. Un
/// <c>CompositionLinearGradientBrush</c> — ou radial — affecté directement au
/// masque est refusé. La version précédente de ce fichier le faisait,
/// l'exception était absorbée par <see cref="TryAttach"/>, et le chemin de
/// rendu de référence retombait silencieusement sur le dégradé XAML. Le dégradé
/// est donc peint dans un visuel hors écran, que <c>CompositionVisualSurface</c>
/// capture en surface : le masque redevient un type accepté, sans dépendance
/// Win2D.
/// </para>
///
/// <para>
/// <b>Pourquoi un dégradé radial.</b> Un dégradé linéaire vertical ne dissout
/// que le bas — les bords gauche et droit restent francs, ce qui fait à nouveau
/// lire l'Island comme une fenêtre posée. L'ellipse est ancrée au <em>milieu du
/// bord supérieur</em> : le haut reste plein d'un bord à l'autre, là où l'Island
/// touche l'écran, puis les côtés se dissipent à mesure que l'on descend, et le
/// bas disparaît entièrement. C'est la dissolution décrite par le langage
/// visuel, pas seulement sa moitié basse.
/// </para>
///
/// Cette classe est volontairement tolérante : si le compositeur refuse la
/// surface, elle se déclare indisponible et l'appelant conserve son repli XAML.
/// Une décoration ne doit jamais empêcher l'Island de s'afficher.
/// </summary>
public sealed class AtmosphericSurface : IDisposable
{
    /// <summary>Bornes du début de fondu. Hors de cet intervalle, la dissolution
    /// n'est plus lisible — soit elle mange la surface, soit elle disparaît.</summary>
    private const double MinFadeStart = 0.05;

    private const double MaxFadeStart = 0.98;

    /// <summary>
    /// Bornes du rayon horizontal de l'ellipse. Le plancher évite qu'un fondu
    /// très précoce ne pince la surface en sablier ; le plafond évite qu'un
    /// fondu très tardif ne rende les côtés parfaitement francs, ce qui
    /// annulerait l'intérêt du radial.
    /// </summary>
    private const float MinRadiusX = 0.55f;

    private const float MaxRadiusX = 3.0f;

    private readonly Compositor _compositor;
    private readonly ContainerVisual _pulse;
    private readonly SpriteVisual _root;
    private readonly SpriteVisual _maskSource;
    private readonly CompositionVisualSurface _maskSurface;
    private readonly CompositionSurfaceBrush _maskBrush;
    private readonly CompositionMaskBrush _masked;
    private readonly CompositionColorBrush _tint;
    private readonly CompositionRadialGradientBrush _falloff;
    private readonly CompositionColorGradientStop _core;
    private readonly CompositionColorGradientStop _edge;

    private bool _disposed;

    private AtmosphericSurface(Compositor compositor, UIElement host)
    {
        // 1. La teinte unie : c'est elle que le masque va percer.
        _tint = compositor.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));

        // 2. Le profil de dissolution, peint dans un visuel hors écran.
        //
        //    L'ellipse est centrée sur le milieu du bord supérieur. Le rayon
        //    vertical vaut exactement 1 : le bas de la surface tombe donc
        //    précisément sur l'arrêt transparent, et aucune ligne ne subsiste.
        _falloff = compositor.CreateRadialGradientBrush();
        _falloff.MappingMode = CompositionMappingMode.Relative;
        _falloff.EllipseCenter = new Vector2(0.5f, 0.0f);
        _falloff.EllipseRadius = new Vector2(MinRadiusX, 1.0f);

        _core = compositor.CreateColorGradientStop(0.62f, Colors.White);
        _edge = compositor.CreateColorGradientStop(1.0f, Colors.Transparent);
        _falloff.ColorStops.Add(_core);
        _falloff.ColorStops.Add(_edge);

        _maskSource = compositor.CreateSpriteVisual();
        _maskSource.Brush = _falloff;

        // 3. Capture du visuel en surface. C'est cette étape qui rend le masque
        //    acceptable par CompositionMaskBrush.
        _maskSurface = compositor.CreateVisualSurface();
        _maskSurface.SourceVisual = _maskSource;
        _maskSurface.SourceOffset = Vector2.Zero;

        _maskBrush = compositor.CreateSurfaceBrush(_maskSurface);
        _maskBrush.Stretch = CompositionStretch.Fill;

        // 4. Teinte masquée. Source : couleur unie. Masque : surface. Les deux
        //    types sont ceux que l'API documente.
        _masked = compositor.CreateMaskBrush();
        _masked.Source = _tint;
        _masked.Mask = _maskBrush;

        _root = compositor.CreateSpriteVisual();
        _root.Brush = _masked;

        // 5. Un conteneur de respiration entre l'hôte et la surface. Son opacité
        //    n'appartient qu'au mouvement hypnotique : elle module la dissolution
        //    sans jamais toucher à l'intensité de base, que SetOpacity continue
        //    de poser sur la surface elle-même. Les deux ne peuvent donc pas se
        //    disputer la même propriété.
        _compositor = compositor;
        _pulse = compositor.CreateContainerVisual();
        _pulse.Children.InsertAtTop(_root);

        ElementCompositionPreview.SetElementChildVisual(host, _pulse);

        IsAvailable = true;
    }

    /// <summary>Surface effectivement attachée à un élément.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// Tente d'attacher une surface atmosphérique à un élément hôte.
    /// </summary>
    /// <returns><c>null</c> si le compositeur refuse la surface.</returns>
    public static AtmosphericSurface? TryAttach(UIElement host)
    {
        ArgumentNullException.ThrowIfNull(host);

        try
        {
            Compositor? compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;

            if (compositor is null)
            {
                return null;
            }

            return new AtmosphericSurface(compositor, host);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Dimensionne la surface et déplace le début du fondu.
    ///
    /// <paramref name="fadeStart"/> est la fraction de rayon encore pleine : à
    /// 0,62 la dissolution occupe les 38 % extérieurs. C'est le paramètre qui
    /// distingue une pilule au repos — fondu court, presque immédiat — d'une
    /// Island ouverte, dont les bords doivent se perdre beaucoup plus loin.
    /// </summary>
    public void Configure(double width, double height, double fadeStart)
    {
        if (_disposed || !IsAvailable || width <= 0 || height <= 0)
        {
            return;
        }

        var size = new Vector2((float)width, (float)height);

        _pulse.Size = size;
        _root.Size = size;
        _maskSource.Size = size;
        _maskSurface.SourceSize = size;

        double clamped = Math.Clamp(fadeStart, MinFadeStart, MaxFadeStart);

        // Un arrêt se déplace, il ne se reconstruit pas : recréer la collection
        // à chaque image de l'animation serait précisément le travail inutile
        // que le projet s'interdit.
        _core.Offset = (float)clamped;

        // Le rayon horizontal est déduit, jamais réglé à part : il place les
        // coins supérieurs exactement sur l'arrêt plein, ce qui garde le bord
        // haut opaque d'un côté à l'autre quelle que soit la profondeur du
        // fondu. Les régler séparément laisserait apparaître une échancrure
        // claire aux deux coins hauts dès que fadeStart descend.
        float radiusX = (float)Math.Clamp(0.5 / clamped, MinRadiusX, MaxRadiusX);
        _falloff.EllipseRadius = new Vector2(radiusX, 1.0f);
    }

    /// <summary>Teinte de la dissolution. Son alpha porte l'intensité.</summary>
    public void SetTint(Color color)
    {
        if (_disposed)
        {
            return;
        }

        _tint.Color = color;
    }

    /// <summary>Opacité globale de la surface, appliquée par le compositeur.</summary>
    public void SetOpacity(double opacity)
    {
        if (_disposed)
        {
            return;
        }

        _root.Opacity = (float)Math.Clamp(opacity, 0.0, 1.0);
    }

    /// <summary>
    /// Fait respirer la dissolution au rythme d'un mouvement hypnotique.
    ///
    /// La respiration est confiée au compositeur sous forme d'images clés
    /// rejouées en boucle : aucune image n'est calculée par le fil d'interface,
    /// et <paramref name="curve"/> vide arrête tout — l'opacité revient à 1.
    /// </summary>
    /// <param name="curve">Couples (avancement de 0 à 1, facteur d'opacité de 0 à 1).</param>
    /// <param name="period">Durée d'un cycle.</param>
    /// <param name="loop">Vrai pour boucler, faux pour un passage unique.</param>
    public void SetPulse(IReadOnlyList<(double Progress, double Factor)> curve, TimeSpan period, bool loop)
    {
        if (_disposed)
        {
            return;
        }

        _pulse.StopAnimation("Opacity");

        if (curve.Count < 2 || period <= TimeSpan.Zero)
        {
            _pulse.Opacity = 1f;
            return;
        }

        ScalarKeyFrameAnimation animation = _compositor.CreateScalarKeyFrameAnimation();
        CompositionEasingFunction linear = _compositor.CreateLinearEasingFunction();

        foreach ((double progress, double factor) in curve)
        {
            animation.InsertKeyFrame(
                (float)Math.Clamp(progress, 0, 1),
                (float)Math.Clamp(factor, 0, 1),
                linear);
        }

        animation.Duration = period;
        animation.IterationBehavior = loop
            ? AnimationIterationBehavior.Forever
            : AnimationIterationBehavior.Count;

        if (!loop)
        {
            animation.IterationCount = 1;
        }

        _pulse.StartAnimation("Opacity", animation);
    }

    /// <summary>Masque entièrement la surface sans la détacher.</summary>
    public void Hide() => SetOpacity(0.0);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsAvailable = false;

        // L'ordre suit le graphe : les consommateurs avant les producteurs. Un
        // visuel peut déjà avoir été libéré avec sa fenêtre, ce qui n'est pas
        // une erreur — d'où la tolérance globale.
        try
        {
            _pulse.StopAnimation("Opacity");
            _pulse.Children.RemoveAll();
            _root.Dispose();
            _pulse.Dispose();
            _masked.Dispose();
            _maskBrush.Dispose();
            _maskSurface.Dispose();
            _maskSource.Dispose();
            _falloff.Dispose();
            _tint.Dispose();
        }
        catch (Exception)
        {
            // Le visuel a pu être libéré avec sa fenêtre : ce n'est pas une erreur.
        }

        GC.SuppressFinalize(this);
    }
}
