using System;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using NotchFlow.Core.Animation;
using NotchFlow.Core.Scenes;

namespace NotchFlow_App.Animations;

/// <summary>
/// Animateur de ressort pour l'Island.
///
/// Le ressort conserve un état continu — position <em>et</em> vitesse. C'est ce
/// qui permet d'infléchir une trajectoire en cours : redémander une cible
/// pendant une animation prolonge le mouvement au lieu de le faire repartir de
/// zéro, ce qui produisait un à-coup visible.
///
/// L'écouteur de rendu est détaché dès que le ressort est stabilisé, ce qui
/// ramène le coût à zéro au repos : rien ne tourne tant que rien ne bouge.
/// </summary>
public sealed class IslandSpringAnimator
{
    /// <summary>
    /// Seuil de repos, en DIPs pour la position et en DIPs par seconde pour la
    /// vitesse. En dessous des deux, l'animation est considérée terminée.
    /// </summary>
    private const double SettleThreshold = 0.35;

    private SpringSolver _solver;
    private readonly Stopwatch _stopwatch = new();
    private readonly Action<IslandFootprint> _onUpdate;
    private readonly Action? _onCompleted;

    private double _fromWidth;
    private double _toWidth;
    private double _fromHeight;
    private double _toHeight;

    private double _width;
    private double _height;
    private double _widthVelocity;
    private double _heightVelocity;

    // Vitesse au démarrage du segment courant. Constante pour toute sa durée,
    // contrairement aux vitesses instantanées ci-dessus.
    private double _initialWidthVelocity;
    private double _initialHeightVelocity;

    private bool _isRunning;

    public IslandSpringAnimator(
        SpringParameters parameters,
        Action<IslandFootprint> onUpdate,
        Action? onCompleted = null)
    {
        _solver = new SpringSolver(parameters);
        _onUpdate = onUpdate ?? throw new ArgumentNullException(nameof(onUpdate));
        _onCompleted = onCompleted;

        _width = IslandFootprint.Collapsed.Width;
        _height = IslandFootprint.Collapsed.Height;
        _fromWidth = _width;
        _fromHeight = _height;
        _toWidth = _width;
        _toHeight = _height;
    }

    /// <summary>Encombrement courant, à mi-parcours compris.</summary>
    public IslandFootprint Current => new(_width, _height);

    /// <summary>
    /// Change la loi du ressort sans interrompre le mouvement en cours.
    ///
    /// Le réglage est modifiable en direct depuis la fenêtre de réglages : la
    /// position et la vitesse courantes sont conservées, seule la trajectoire
    /// restante change. Repartir de zéro produirait un à-coup visible au moment
    /// précis où l'utilisateur ajuste le curseur.
    /// </summary>
    public void UpdateParameters(SpringParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        _solver = new SpringSolver(parameters);

        if (!_isRunning)
        {
            return;
        }

        _fromWidth = _width;
        _fromHeight = _height;
        // La vitesse initiale du segment est figée ici. La réinjecter à chaque
        // image alors que t continue de courir depuis la même origine fausse la
        // trajectoire : la solution analytique attend un couple (position,
        // vitesse) constant sur toute la durée du segment.
        _initialWidthVelocity = _widthVelocity;
        _initialHeightVelocity = _heightVelocity;

        _stopwatch.Restart();
    }

    /// <summary>
    /// Vrai tant qu'un rendu par image est nécessaire. Sert à prouver le
    /// « 0 % au repos » dans les diagnostics.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Nombre d'images effectivement rendues depuis le démarrage du processus.
    /// </summary>
    public long RenderedFrames { get; private set; }

    /// <summary>
    /// Oriente le ressort vers un nouvel encombrement en conservant la position
    /// et la vitesse courantes.
    /// </summary>
    public void AnimateTo(IslandFootprint target)
    {
        if (!target.IsValid)
        {
            return;
        }

        // Instantané de l'état continu : c'est le point clé de la reprise.
        _fromWidth = _width;
        _fromHeight = _height;
        _toWidth = target.Width;
        _toHeight = target.Height;

        // La vitesse initiale du segment est figée ici. La réinjecter à chaque
        // image alors que t continue de courir depuis la même origine fausse la
        // trajectoire : la solution analytique attend un couple (position,
        // vitesse) constant sur toute la durée du segment.
        _initialWidthVelocity = _widthVelocity;
        _initialHeightVelocity = _heightVelocity;

        _stopwatch.Restart();

        if (!_isRunning)
        {
            _isRunning = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    /// <summary>
    /// Applique un encombrement immédiatement, sans animation. Utilisé à
    /// l'initialisation et lorsque Windows demande la réduction des animations.
    /// </summary>
    public void SnapTo(IslandFootprint footprint)
    {
        Stop();

        _width = _fromWidth = _toWidth = footprint.Width;
        _height = _fromHeight = _toHeight = footprint.Height;
        _widthVelocity = 0;
        _heightVelocity = 0;
        _initialWidthVelocity = 0;
        _initialHeightVelocity = 0;

        _onUpdate(footprint);
    }

    private void OnRendering(object? sender, object e)
    {
        double t = _stopwatch.Elapsed.TotalSeconds;

        (double width, double widthVelocity) = _solver.Evaluate(t, _fromWidth, _toWidth, _initialWidthVelocity);
        (double height, double heightVelocity) = _solver.Evaluate(t, _fromHeight, _toHeight, _initialHeightVelocity);

        _width = width;
        _height = height;
        _widthVelocity = widthVelocity;
        _heightVelocity = heightVelocity;

        RenderedFrames++;

        _onUpdate(new IslandFootprint(width, height));

        bool settled = Math.Abs(width - _toWidth) < SettleThreshold
            && Math.Abs(height - _toHeight) < SettleThreshold
            && Math.Abs(widthVelocity) < SettleThreshold
            && Math.Abs(heightVelocity) < SettleThreshold;

        if (!settled)
        {
            return;
        }

        IslandFootprint target = new(_toWidth, _toHeight);

        _width = _fromWidth = _toWidth = target.Width;
        _height = _fromHeight = _toHeight = target.Height;
        _widthVelocity = 0;
        _heightVelocity = 0;
        _initialWidthVelocity = 0;
        _initialHeightVelocity = 0;

        Stop();

        _onUpdate(target);
        _onCompleted?.Invoke();
    }

    /// <summary>Détache immédiatement l'écouteur de rendu.</summary>
    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        CompositionTarget.Rendering -= OnRendering;
        _stopwatch.Stop();
    }
}
