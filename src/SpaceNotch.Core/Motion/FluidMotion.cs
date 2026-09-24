using System;
using SpaceNotch.Core.Animation;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Physique des gestes directs : résistance élastique, projection d'élan et
/// étirement selon la vitesse.
///
/// <para>
/// Les formules sont celles des interfaces fluides d'Apple (WWDC 2018, session
/// 803, « Designing Fluid Interfaces ») : un objet saisi suit la main, garde son
/// élan quand on le lâche, et ne s'arrête jamais sur un à-coup. Voir ADR-019.
/// </para>
/// </summary>
public static class FluidMotion
{
    /// <summary>Constante de résistance élastique d'UIScrollView : 0,55.</summary>
    public const double RubberBandConstant = 0.55;

    /// <summary>Décélération « normale » d'un défilement : l'élan porte loin.</summary>
    public const double NormalDeceleration = 0.998;

    /// <summary>Décélération « rapide » : l'élan s'éteint vite.</summary>
    public const double FastDeceleration = 0.99;

    /// <summary>Étirement maximal d'une surface d'interface : 5 %, au-delà elle devient un jouet.</summary>
    public const double MaximumStretch = 0.05;

    /// <summary>Vitesse, en DIPs par seconde, à laquelle l'étirement atteint son maximum.</summary>
    public const double FullStretchSpeed = 2800;

    /// <summary>
    /// Résistance élastique : plus on tire, moins l'objet suit. La courbe part
    /// avec une pente de <paramref name="constant"/> et tend vers
    /// <paramref name="dimension"/> sans jamais l'atteindre.
    /// </summary>
    /// <param name="offset">Déplacement demandé par la main, signé.</param>
    /// <param name="dimension">Déplacement visuel asymptotique.</param>
    /// <param name="constant">Pente initiale.</param>
    public static double RubberBand(double offset, double dimension, double constant = RubberBandConstant)
    {
        if (dimension <= 0 || offset == 0 || double.IsNaN(offset))
        {
            return 0;
        }

        double magnitude = Math.Abs(offset);
        double resisted = magnitude * dimension * constant / (dimension + (constant * magnitude));

        return Math.CopySign(resisted, offset);
    }

    /// <summary>
    /// Distance que parcourrait un objet lâché à cette vitesse, s'il décélérait
    /// comme un défilement. C'est la décroissance exponentielle d'UIScrollView,
    /// et non la formule de manuel <c>v²/2a</c> : c'est elle que l'œil connaît.
    /// </summary>
    /// <param name="velocity">Vitesse au lâcher, en DIPs par seconde, signée.</param>
    /// <param name="decelerationRate">Taux de décélération par milliseconde, dans ]0, 1[.</param>
    public static double Project(double velocity, double decelerationRate = NormalDeceleration)
    {
        if (double.IsNaN(velocity) || decelerationRate <= 0 || decelerationRate >= 1)
        {
            return 0;
        }

        return velocity / 1000.0 * decelerationRate / (1 - decelerationRate);
    }

    /// <summary>
    /// Étirement d'une surface qui se déplace : allongée dans le sens de la
    /// course, amincie dans l'autre, à aire constante — une matière qui se
    /// déforme sans gonfler.
    ///
    /// <para>
    /// La surface reste alignée sur les axes : sur une diagonale parfaite les
    /// deux étirements s'annulent, et la forme garde ses proportions. Tourner la
    /// forme dans le sens de la course la ferait pencher, ce qu'une notch ne fait
    /// jamais.
    /// </para>
    /// </summary>
    /// <returns>Facteurs d'échelle horizontal et vertical ; leur produit vaut 1.</returns>
    public static (double ScaleX, double ScaleY) Stretch(
        double velocityX,
        double velocityY,
        double maximum = MaximumStretch,
        double fullSpeed = FullStretchSpeed)
    {
        double speed = Math.Sqrt((velocityX * velocityX) + (velocityY * velocityY));

        if (speed < 1e-6 || maximum <= 0 || fullSpeed <= 0 || double.IsNaN(speed))
        {
            return (1, 1);
        }

        double amount = Math.Min(1, speed / fullSpeed) * maximum;
        double cos2 = velocityX * velocityX / (speed * speed);
        double exponent = Math.Log(1 + amount) * ((2 * cos2) - 1);

        return (Math.Exp(exponent), Math.Exp(-exponent));
    }
}

/// <summary>
/// Ressort à deux dimensions qui <em>suit</em> une cible mobile.
///
/// <para>
/// Le résolveur analytique de l'Island suppose une cible fixe pendant tout un
/// segment. Ici la cible est le pointeur, qui bouge à chaque image : le ressort
/// est donc intégré pas à pas (Euler semi-implicite, sous-pas de 1/480 s), ce
/// qui reste stable quelle que soit la cadence d'affichage et laisse la
/// position et la vitesse continues quand la cible saute — c'est ce qui rend
/// le geste interruptible à tout instant.
/// </para>
/// </summary>
public sealed class Spring2D
{
    /// <summary>Écart de repos, en DIPs.</summary>
    public const double RestDistance = 0.3;

    /// <summary>Vitesse de repos, en DIPs par seconde.</summary>
    public const double RestSpeed = 4;

    private const double MaximumStep = 1.0 / 480;

    private readonly double _restDistance;
    private readonly double _restSpeed;

    private double _stiffness;
    private double _damping;

    /// <param name="parameters">Loi du ressort.</param>
    /// <param name="x">Position initiale.</param>
    /// <param name="y">Position initiale.</param>
    /// <param name="restDistance">Écart sous lequel le ressort est au repos, dans l'unité de la position.</param>
    /// <param name="restSpeed">Vitesse sous laquelle le ressort est au repos, par seconde.</param>
    public Spring2D(
        SpringParameters parameters,
        double x = 0,
        double y = 0,
        double restDistance = RestDistance,
        double restSpeed = RestSpeed)
    {
        _restDistance = restDistance > 0 ? restDistance : RestDistance;
        _restSpeed = restSpeed > 0 ? restSpeed : RestSpeed;
        SetParameters(parameters);
        X = TargetX = x;
        Y = TargetY = y;
    }

    public double X { get; private set; }

    public double Y { get; private set; }

    public double VelocityX { get; private set; }

    public double VelocityY { get; private set; }

    public double TargetX { get; private set; }

    public double TargetY { get; private set; }

    /// <summary>Vrai quand la position a rejoint la cible et que le mouvement s'est éteint.</summary>
    public bool IsSettled
        => Math.Abs(X - TargetX) < _restDistance
            && Math.Abs(Y - TargetY) < _restDistance
            && Math.Abs(VelocityX) < _restSpeed
            && Math.Abs(VelocityY) < _restSpeed;

    /// <summary>Change la loi du ressort sans toucher à la position ni à la vitesse.</summary>
    public void SetParameters(SpringParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        double mass = Math.Max(0.1, parameters.Mass);
        _stiffness = Math.Max(0.1, parameters.Stiffness) / mass;
        _damping = Math.Max(0, parameters.Damping) / mass;
    }

    public void SetTarget(double x, double y)
    {
        TargetX = x;
        TargetY = y;
    }

    /// <summary>Transmet une vitesse — celle de la main au lâcher, en DIPs par seconde.</summary>
    public void SetVelocity(double velocityX, double velocityY)
    {
        VelocityX = velocityX;
        VelocityY = velocityY;
    }

    /// <summary>Place le ressort immédiatement, sans mouvement.</summary>
    public void Snap(double x, double y)
    {
        X = TargetX = x;
        Y = TargetY = y;
        VelocityX = 0;
        VelocityY = 0;
    }

    /// <summary>Avance le ressort de <paramref name="seconds"/>.</summary>
    public void Step(double seconds)
    {
        if (!(seconds > 0))
        {
            return;
        }

        // Une image perdue — une fenêtre déplacée, un écran qui se réveille —
        // ne doit pas faire exploser l'intégration : au-delà de 1/15 s, le
        // temps est simplement tronqué.
        double remaining = Math.Min(seconds, 1.0 / 15);

        while (remaining > 0)
        {
            double dt = Math.Min(MaximumStep, remaining);
            remaining -= dt;

            double ax = (-_stiffness * (X - TargetX)) - (_damping * VelocityX);
            double ay = (-_stiffness * (Y - TargetY)) - (_damping * VelocityY);

            VelocityX += ax * dt;
            VelocityY += ay * dt;
            X += VelocityX * dt;
            Y += VelocityY * dt;
        }

        if (IsSettled)
        {
            Snap(TargetX, TargetY);
        }
    }
}

/// <summary>
/// Ressort scalaire intégré pas à pas : l'élan du « pop » d'arrachement, le
/// rebond de la bulle. Même intégrateur que <see cref="Spring2D"/>.
/// </summary>
public sealed class Spring1D
{
    private readonly Spring2D _spring;

    /// <param name="parameters">Loi du ressort.</param>
    /// <param name="value">Valeur initiale.</param>
    /// <param name="restDistance">Écart de repos : un millième convient à une échelle.</param>
    /// <param name="restSpeed">Vitesse de repos, par seconde.</param>
    public Spring1D(SpringParameters parameters, double value = 0, double restDistance = 0.001, double restSpeed = 0.01)
    {
        _spring = new Spring2D(parameters, value, 0, restDistance, restSpeed);
    }

    public double Value => _spring.X;

    public double Velocity => _spring.VelocityX;

    public double Target => _spring.TargetX;

    public bool IsSettled => _spring.IsSettled;

    public void SetTarget(double target) => _spring.SetTarget(target, 0);

    public void SetVelocity(double velocity) => _spring.SetVelocity(velocity, 0);

    public void Snap(double value) => _spring.Snap(value, 0);

    public void Step(double seconds) => _spring.Step(seconds);
}

/// <summary>
/// Vitesse de la main, estimée sur ses derniers échantillons.
///
/// <para>
/// La dernière différence entre deux événements ne suffit pas : deux
/// événements de souris peuvent arriver dans la même milliseconde, ou la main
/// peut s'arrêter une image avant de lâcher. La vitesse est donc mesurée sur
/// une fenêtre glissante d'environ 80 ms — et une main immobile depuis plus de
/// 60 ms au lâcher n'a plus d'élan, quoi qu'elle ait fait avant.
/// </para>
/// </summary>
public sealed class VelocityTracker
{
    /// <summary>Durée sur laquelle la vitesse est mesurée, en secondes.</summary>
    public const double Window = 0.08;

    /// <summary>Immobilité au-delà de laquelle l'élan est considéré perdu, en secondes.</summary>
    public const double StaleAfter = 0.06;

    private const int Capacity = 32;

    private readonly (double Time, double X, double Y)[] _samples = new (double, double, double)[Capacity];
    private int _count;
    private int _head;

    /// <summary>Oublie tous les échantillons.</summary>
    public void Reset()
    {
        _count = 0;
        _head = 0;
    }

    /// <summary>Ajoute une position, horodatée en secondes.</summary>
    public void Add(double time, double x, double y)
    {
        _samples[_head] = (time, x, y);
        _head = (_head + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
    }

    /// <summary>Vitesse en unités par seconde à l'instant <paramref name="now"/>.</summary>
    public (double X, double Y) Velocity(double now)
    {
        if (_count < 2)
        {
            return (0, 0);
        }

        (double Time, double X, double Y) last = _samples[(_head - 1 + Capacity) % Capacity];

        if (now - last.Time > StaleAfter)
        {
            return (0, 0);
        }

        (double Time, double X, double Y) first = last;

        for (int i = 2; i <= _count; i++)
        {
            (double Time, double X, double Y) sample = _samples[(_head - i + Capacity) % Capacity];

            if (last.Time - sample.Time > Window)
            {
                break;
            }

            first = sample;
        }

        double dt = last.Time - first.Time;

        if (dt < 1e-3)
        {
            return (0, 0);
        }

        return ((last.X - first.X) / dt, (last.Y - first.Y) / dt);
    }
}
