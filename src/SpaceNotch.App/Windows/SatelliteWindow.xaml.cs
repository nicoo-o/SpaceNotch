using System;
using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Animation;
using SpaceNotch.Platform.Windows.Windowing;
using SpaceNotch_App.Composition;
using SpaceNotch_App.Views;
using Windows.Graphics;
using WinRT.Interop;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Satellite détaché : un second lobe, à droite de l'Island, lorsque plusieurs
/// activités vivent en même temps.
///
/// <para>
/// <b>Pourquoi une fenêtre séparée.</b> Windows ne fait traverser
/// <c>HTTRANSPARENT</c> qu'entre fenêtres d'un même thread : une fenêtre unique
/// contenant l'Island, le vide et le satellite avalerait donc les clics destinés à
/// ce qui se trouve dans ce vide. En donnant au satellite sa propre fenêtre, le
/// vide n'appartient à personne et les clics y passent naturellement — sans
/// bricolage de test de collision, et sans le crénelage qu'imposerait une région
/// de fenêtre. Voir ADR-015.
/// </para>
///
/// <para>
/// <b>Ce qu'il montre.</b> Le glyphe de l'activité <em>suivante</em>, teinté par son
/// état, exactement comme le glyphe de tête de l'Island : c'est un aperçu, pas un
/// compteur. Cliquer dessus présente cette activité.
/// </para>
/// </summary>
public sealed partial class SatelliteWindow : Window
{
    /// <summary>Diamètre du disque, en DIPs. Vaut exactement deux fois le rayon des congés.</summary>
    private const double DiameterDip = 24;

    /// <summary>Écart entre l'Island et le satellite, en DIPs.</summary>
    private const double GapDip = 8;

    /// <summary>Course de l'apparition, en DIPs.</summary>
    private const double TravelDip = 8;

    /// <summary>
    /// Ressort de l'apparition : plus vif et plus élastique que celui de l'Island.
    ///
    /// Un petit objet se déplace plus vite qu'un grand, et le satellite doit
    /// paraître léger — c'est ce qui fait qu'on le lit comme une bulle détachée
    /// plutôt que comme un second panneau.
    /// </summary>
    private static readonly SpringParameters ArrivalSpring = SpringParameters.FromResponse(0.32, 0.58);

    private readonly IntPtr _hWnd;
    private readonly AppWindow _appWindow;
    private readonly SpringSolver _arrivalSolver = new(ArrivalSpring);
    private readonly Stopwatch _arrivalClock = new();

    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;

    private double _arrival;
    private double _arrivalTarget;
    private bool _animating;
    private bool _shown;

    /// <summary>
    /// La fenêtre est-elle réellement affichée ? Distinct de <see cref="IsShown"/>,
    /// qui décrit l'intention : pendant le retrait, l'intention est déjà partie
    /// alors que la fenêtre est encore à l'écran, le temps de l'animation.
    /// </summary>
    private bool _windowShown;

    public SatelliteWindow()
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

        // Mêmes styles que l'Island : hors barre des tâches, hors Alt+Tab, et sans
        // jamais prendre le focus. Cliquer le satellite ne perturbe donc pas la
        // fenêtre au premier plan, et le clavier reste acquis à l'Island.
        WindowChrome.ApplyInteractiveSurface(_hWnd);

        Closed += OnClosed;
    }

    /// <summary>
    /// Signalé lorsque l'utilisateur clique le satellite.
    ///
    /// Le nom ne peut pas être « Activated » : <c>Window</c> en expose déjà un,
    /// pour l'activation de la fenêtre, et les deux n'ont rien à voir. Les
    /// confondre reviendrait à faire passer un clic pour un changement de focus.
    /// </summary>
    public event EventHandler? Clicked;

    /// <summary>Vrai lorsque le satellite est effectivement à l'écran.</summary>
    public bool IsShown => _shown;

    /// <summary>
    /// Met le satellite à jour. <paramref name="activity"/> à <c>null</c> le
    /// retire de l'écran.
    /// </summary>
    public void Apply(IslandActivity? activity)
    {
        if (activity is null)
        {
            SetVisible(false);
            return;
        }

        SatelliteGlyph.Glyph = GlyphCatalog.Resolve(activity.IconKey);
        SatelliteGlyph.Foreground = StatePalette.Brush(activity.State);

        SetVisible(true);
    }

    /// <summary>
    /// Place le satellite à droite de l'Island, centré sur sa hauteur.
    /// </summary>
    /// <param name="islandX">Abscisse physique de l'Island.</param>
    /// <param name="islandY">Ordonnée physique de l'Island.</param>
    /// <param name="islandWidthPx">Largeur physique de l'Island.</param>
    /// <param name="islandHeightPx">Hauteur physique de l'Island.</param>
    public void PlaceAround(int islandX, int islandY, int islandWidthPx, int islandHeightPx)
    {
        double scale = Scale;

        int diameter = (int)Math.Round(DiameterDip * scale);
        int gap = (int)Math.Round(GapDip * scale);

        Disc.Width = DiameterDip;
        Disc.Height = DiameterDip;

        int x = islandX + islandWidthPx + gap;
        int y = islandY + ((islandHeightPx - diameter) / 2);

        if (x == _lastX && y == _lastY)
        {
            return;
        }

        _lastX = x;
        _lastY = y;

        _appWindow.MoveAndResize(new RectInt32(x, y, diameter, diameter));
    }

    /// <summary>Échelle du moniteur, lue sur le contenu plutôt que reçue.</summary>
    private double Scale
    {
        get
        {
            double scale = SatelliteRoot.XamlRoot?.RasterizationScale ?? 1.0;
            return scale <= 0 ? 1.0 : scale;
        }
    }

    /// <summary>
    /// Affiche ou retire le satellite, en le faisant glisser depuis l'Island.
    ///
    /// Aucun minuteur ne subsiste : la boucle de rendu s'arrête d'elle-même dès
    /// que le ressort est stabilisé, et le satellite immobile ne coûte plus rien.
    /// </summary>
    private void SetVisible(bool visible)
    {
        if (visible == _shown && Math.Abs(_arrivalTarget - (visible ? 1 : 0)) < 0.01)
        {
            return;
        }

        _shown = visible;
        _arrivalTarget = visible ? 1.0 : 0.0;

        if (visible && !_windowShown)
        {
            _appWindow.Show(activateWindow: false);
            _windowShown = true;
        }

        if (!UseSpringAnimations)
        {
            ApplyArrival(_arrivalTarget);

            if (!visible)
            {
                _appWindow.Hide();
                _windowShown = false;
            }

            return;
        }

        StartArrival();
    }

    /// <summary>Les animations sont autorisées ? Renseigné par la fenêtre principale.</summary>
    public bool UseSpringAnimations { get; set; } = true;

    private void StartArrival()
    {
        _arrivalClock.Restart();

        if (_animating)
        {
            return;
        }

        _animating = true;
        CompositionTarget.Rendering += OnArrivalFrame;
    }

    private void OnArrivalFrame(object? sender, object e)
    {
        double t = _arrivalClock.Elapsed.TotalSeconds;

        (double progress, double velocity) = _arrivalSolver.Evaluate(
            t,
            startValue: _arrival,
            targetValue: _arrivalTarget,
            initialVelocity: 0.0);

        ApplyArrival(progress);

        if (Math.Abs(progress - _arrivalTarget) > 0.01 || Math.Abs(velocity) > 0.02)
        {
            return;
        }

        CompositionTarget.Rendering -= OnArrivalFrame;
        _animating = false;
        _arrivalClock.Stop();

        ApplyArrival(_arrivalTarget);

        if (_arrivalTarget <= 0)
        {
            _appWindow.Hide();
            _windowShown = false;
        }
    }

    private void ApplyArrival(double progress)
    {
        _arrival = Math.Clamp(progress, 0.0, 1.0);

        // Le déplacement vient de l'Island, d'où le signe négatif : le satellite
        // se détache en s'éloignant, et se retire en revenant vers elle.
        DiscShift.X = -TravelDip * (1 - _arrival);
        SatelliteRoot.Opacity = _arrival;
    }

    private void OnSatellitePressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        Clicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_animating)
        {
            CompositionTarget.Rendering -= OnArrivalFrame;
            _animating = false;
        }

        _arrivalClock.Stop();
    }
}
