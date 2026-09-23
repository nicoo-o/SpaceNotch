using System;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NotchFlow.Core.Animation;
using NotchFlow.Platform.Windows.Windowing;
using NotchFlow_App.Composition;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace NotchFlow_App.Windows;

/// <summary>
/// Fenêtre décorative de l'Island : elle porte la dissolution et le halo.
///
/// Elle existe séparément parce que WinUI 3 ne permet pas de rendre une région
/// transparente clic-traversante (<c>SetLayeredWindowAttributes(LWA_COLORKEY)</c>
/// y est inopérant, et <c>SetWindowRgn</c> clipperait le fondu). En isolant la
/// décoration dans une fenêtre marquée <c>WS_EX_TRANSPARENT</c>, la dissolution
/// devient possible sans jamais voler un clic à l'utilisateur.
///
/// La fenêtre ne décide de rien : elle reçoit une boîte et une teinte, et les
/// projette. La profondeur du fondu comme la teinte lui sont fournies par
/// l'activité présentée.
/// </summary>
public sealed partial class AtmosphereWindow : Window
{
    /// <summary>Débord horizontal du fondu, en pixels physiques.</summary>
    private const int HorizontalBleedPhysical = 64;

    /// <summary>
    /// Profondeur minimale du fondu, en pixels physiques. S'applique à la pilule
    /// au repos, dont le bas doit se dissiper court.
    /// </summary>
    private const int MinimumBottomBleedPhysical = 56;

    /// <summary>
    /// Proportion de la hauteur de l'Island ajoutée sous elle pour la
    /// dissolution : une Island ouverte se dissout beaucoup plus loin qu'une
    /// pilule fermée. C'est ce rapport, et non une constante, qui fait que la
    /// géométrie du fondu suit la scène.
    /// </summary>
    private const double BleedHeightRatio = 0.72;

    /// <summary>
    /// Ressort de teinte, volontairement plus mou que celui de l'Island.
    ///
    /// Ce décalage est le détail qui fait la différence entre un changement de
    /// couleur mécanique et un souffle : la lumière prend sa nouvelle teinte
    /// quelques dixièmes de seconde après que la forme s'est déplacée.
    /// </summary>
    private static readonly SpringParameters TintSpring = new(Stiffness: 70.0, Damping: 16.0, Mass: 1.0);

    private readonly IntPtr _hWnd;
    private readonly AppWindow _appWindow;
    private readonly SpringSolver _tintSolver = new(TintSpring);
    private readonly Stopwatch _tintClock = new();

    private AtmosphericSurface? _surface;
    private IslandShadowSurface? _shadow;

    /// <summary>Intensité demandée du halo, avant pondération par le déploiement.</summary>
    private double _glowIntensity;

    /// <summary>Avancement du déploiement, de 0 au repos à 1 ouvert.</summary>
    private double _deployment;

    // Dernier rectangle physique réellement soumis au gestionnaire de fenêtres.
    // Sert uniquement à ne pas le resoumettre à l'identique (voir PositionAround).
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private int _lastWidth = int.MinValue;
    private int _lastHeight = int.MinValue;

    private Color _tintFrom = Colors.Transparent;
    private Color _tintCurrent = Colors.Transparent;
    private Color _tintTarget = Colors.Transparent;
    private bool _tintRunning;

    /// <summary>Vrai lorsque la dissolution est portée par le compositeur.</summary>
    public bool UsesCompositionSurface => _surface?.IsAvailable == true;

    /// <summary>Vrai lorsque l'ombre portée est portée par le compositeur.</summary>
    public bool UsesCompositionShadow => _shadow?.IsAvailable == true;

    public AtmosphereWindow()
    {
        InitializeComponent();

        _hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        WindowChrome.ApplyAtmosphereSurface(_hWnd);

        // Si le compositeur refuse la surface, la dissolution reste assurée par le
        // dégradé XAML du halo : l'appelant n'a rien à faire de particulier.
        _surface = AtmosphericSurface.TryAttach(FadeHost);

        // L'ombre est tentée séparément : elle ne dépend pas de la même capacité
        // que la dissolution, et un échec de l'une ne doit pas emporter l'autre.
        _shadow = IslandShadowSurface.TryAttach(ShadowHost);

        ApplyFallbackSurface();

        _appWindow.Show();

        Closed += OnClosed;
    }

    /// <summary>Handle natif de la fenêtre décorative.</summary>
    public IntPtr Handle => _hWnd;

    /// <summary>
    /// Active ou désactive la dissolution portée par le compositeur.
    ///
    /// Désactivée, la dissolution retombe sur le dégradé XAML du halo. Le
    /// basculement est réversible — la surface est recréée à la demande — ce qui
    /// permet de comparer les deux voies sans relancer l'application.
    /// </summary>
    public void SetCompositionEnabled(bool enabled)
    {
        if (enabled)
        {
            _surface ??= AtmosphericSurface.TryAttach(FadeHost);
            ApplyFallbackSurface();
            return;
        }

        _surface?.Dispose();
        _surface = null;

        ApplyFallbackSurface();
    }

    /// <summary>
    /// Choisit qui peint la dissolution : le compositeur, ou le dégradé XAML.
    ///
    /// Jamais les deux : superposer les deux voies doublerait l'intensité du fondu
    /// et le rendu ne correspondrait plus à aucun des deux réglages. Le repli n'est
    /// donc peint que lorsque la surface composée n'existe pas.
    /// </summary>
    private void ApplyFallbackSurface()
        => FadeHost.Background = _surface?.IsAvailable == true
            ? null
            : AtmosphericMaskHelper.CreateAtmosphericGradient();

    /// <summary>
    /// Aligne la couche décorative sur le corps de l'Island, en la débordant de
    /// manière asymétrique : large sur les côtés et surtout vers le bas, pour que
    /// le fondu ait de la place pour se dissiper.
    /// </summary>
    public void PositionAround(AtmospherePlacement placement)
    {
        int bleed = Math.Max(
            MinimumBottomBleedPhysical,
            (int)(placement.HeightPx * BleedHeightRatio));

        int x = placement.X - HorizontalBleedPhysical;
        int y = placement.Y;
        int width = placement.WidthPx + (2 * HorizontalBleedPhysical);
        int height = placement.HeightPx + bleed;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        _deployment = Math.Clamp(placement.Deployment, 0.0, 1.0);

        // Le ressort produit 60 à 120 images par seconde, et beaucoup d'entre
        // elles retombent sur le même rectangle en pixels entiers — surtout en
        // fin de course, où les écarts passent sous le pixel. Sans ce test,
        // chacune de ces images enchaîne un SetWindowPos et une reprise de
        // composition DWM pour un résultat rigoureusement identique.
        //
        // Seul l'appel au gestionnaire de fenêtres est filtré. Le halo et la
        // surface de dissolution continuent d'être configurés à chaque passage,
        // parce qu'ils peuvent avoir été recréés entre-temps — par
        // SetCompositionEnabled — sans que la géométrie, elle, ait bougé.
        if (x != _lastX || y != _lastY || width != _lastWidth || height != _lastHeight)
        {
            _lastX = x;
            _lastY = y;
            _lastWidth = width;
            _lastHeight = height;

            _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }

        // Tout ce qui suit vit dans l'espace du contenu, qui est en DIPs — et non
        // dans celui du gestionnaire de fenêtres. La conversion n'est pas
        // cosmétique : à 150 % d'échelle, confondre les deux fait dessiner un halo
        // et une ombre d'un tiers trop grands.
        double scale = Scale;
        double contentHeight = height / scale;

        // Le halo déborde sous l'Island : il est centré sur sa partie haute, puis
        // étiré vers le bas.
        double glowWidth = placement.WidthDip * 1.35;
        double glowHeight = contentHeight * 0.92;

        Glow.SetGlowSize(glowWidth, glowHeight);
        Glow.SetGlowOffset(placement.HeightDip * 0.12);

        // L'ombre épouse la forme réelle et suit le morphing image par image :
        // elle n'est jamais animée à part, sans quoi elle finirait par se
        // désynchroniser de la forme qu'elle décrit.
        ShadowHost.Width = placement.WidthDip;
        ShadowHost.Height = placement.HeightDip;

        _shadow?.Configure(placement.WidthDip, placement.HeightDip, placement.CornerRadiusDip);
        _shadow?.SetDeployment(_deployment);

        // La dissolution n'existe qu'au déploiement : au repos l'Island est un
        // objet au contour franc, et c'est l'ombre qui le sépare du bureau.
        FadeHost.Width = glowWidth;
        FadeHost.Height = contentHeight;
        FadeHost.Opacity = _deployment;

        if (_surface is not null)
        {
            // Le fondu commence juste au-dessus du bas du corps : les derniers
            // pourcents du corps se dissolvent eux aussi, ce qui supprime l'arête.
            double fadeStart = Math.Clamp(placement.HeightDip * 0.35 / contentHeight, 0.10, 0.90);

            _surface.Configure(glowWidth, contentHeight, fadeStart);
            _surface.SetOpacity(_glowIntensity * _deployment);
        }
    }

    /// <summary>
    /// Échelle du moniteur, lue sur le contenu plutôt que reçue : la couche
    /// décorative n'a aucune raison de dépendre d'un paramètre qu'elle peut
    /// constater elle-même.
    /// </summary>
    private double Scale
    {
        get
        {
            double scale = AtmosphereRoot.XamlRoot?.RasterizationScale ?? 1.0;
            return scale <= 0 ? 1.0 : scale;
        }
    }

    /// <summary>
    /// Change la teinte du halo et de la dissolution, avec un ressort plus mou
    /// que celui de l'Island.
    /// </summary>
    public void SetGlowColor(Color color)
    {
        if (ColorsEqual(color, _tintTarget))
        {
            return;
        }

        _tintFrom = _tintCurrent;
        _tintTarget = color;

        if (UseSpringAnimations)
        {
            StartTintTransition();
            return;
        }

        ApplyTint(1.0);
    }

    /// <summary>
    /// Les animations sont-elles autorisées ? Renseigné par la fenêtre principale,
    /// qui seule connaît les préférences système et utilisateur.
    /// </summary>
    public bool UseSpringAnimations { get; set; } = true;

    /// <summary>Intensité du halo. Volontairement faible : une suggestion, pas un signal.</summary>
    public void SetGlowIntensity(double opacity, double tintOpacity)
    {
        _glowIntensity = opacity;

        Glow.SetGlowIntensity(opacity, tintOpacity);

        // La dissolution suit la même intensité que le halo, pondérée par le
        // déploiement : deux réglages indépendants finiraient par diverger
        // visuellement.
        _surface?.SetOpacity(_glowIntensity * _deployment);
    }

    /// <summary>Rétablit l'ordre attendu : le corps interactif reste au-dessus.</summary>
    public void PlaceBehind(IntPtr interactiveHandle) => WindowChrome.PlaceAbove(interactiveHandle, _hWnd);

    /// <summary>
    /// Montre ou retire la couche décorative.
    ///
    /// Elle suit l'Island et ne décide jamais seule : une ombre qui resterait
    /// posée sur un écran occupé par un jeu serait un artefact visible, et une
    /// dissolution sans forme au-dessus d'elle serait un nuage sans objet.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (visible)
        {
            _appWindow.Show();
            return;
        }

        _appWindow.Hide();
    }

    private void StartTintTransition()
    {
        // Le résolveur est une fonction pure du temps écoulé : redémarrer
        // l'horloge suffit à repartir de la teinte courante.
        _tintClock.Restart();

        if (_tintRunning)
        {
            return;
        }

        _tintRunning = true;
        CompositionTarget.Rendering += OnTintFrame;
    }

    private void OnTintFrame(object? sender, object e)
    {

        double t = _tintClock.Elapsed.TotalSeconds;

        (double progress, double velocity) = _tintSolver.Evaluate(
            t,
            startValue: 0.0,
            targetValue: 1.0,
            initialVelocity: 0.0);

        double clamped = Math.Clamp(progress, 0.0, 1.0);
        ApplyTint(clamped);

        bool settled = Math.Abs(progress - 1.0) < 0.01 && Math.Abs(velocity) < 0.02;

        if (!settled)
        {
            return;
        }

        // Arrêt explicite : c'est ce qui ramène le coût à zéro une fois la teinte
        // posée. Aucun écouteur de rendu ne subsiste au repos.
        CompositionTarget.Rendering -= OnTintFrame;
        _tintRunning = false;
        _tintClock.Stop();

        ApplyTint(1.0);
    }

    private void ApplyTint(double progress)
    {
        Color color = Lerp(_tintFrom, _tintTarget, progress);

        _tintCurrent = color;

        Glow.SetGlowColor(color);
        _surface?.SetTint(color);
    }

    private static Color Lerp(Color from, Color to, double progress)
    {
        byte a = (byte)(from.A + ((to.A - from.A) * progress));
        byte r = (byte)(from.R + ((to.R - from.R) * progress));
        byte g = (byte)(from.G + ((to.G - from.G) * progress));
        byte b = (byte)(from.B + ((to.B - from.B) * progress));

        return Color.FromArgb(a, r, g, b);
    }

    private static bool ColorsEqual(Color left, Color right)
        => left.A == right.A && left.R == right.R && left.G == right.G && left.B == right.B;

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_tintRunning)
        {
            CompositionTarget.Rendering -= OnTintFrame;
            _tintRunning = false;
        }

        _tintClock.Stop();
        _surface?.Dispose();
        _surface = null;
        _shadow?.Dispose();
        _shadow = null;
    }
}
