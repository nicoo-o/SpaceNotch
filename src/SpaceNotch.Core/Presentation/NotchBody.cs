using System;

namespace SpaceNotch.Core.Presentation;

/// <summary>
/// La notch lancée (P3) : un corps avec une position, une vitesse et un poids.
///
/// <para>
/// Détachée et lancée fort, la pastille garde son élan : elle glisse, perd de
/// la vitesse par frottement, rebondit contre les bords de la zone de travail
/// en rendant une part de son énergie, et s'écrase un peu à chaque choc. Une
/// fois l'élan épuisé, l'atterrissage habituel (aimant, pose) reprend la main.
/// </para>
///
/// <para>
/// Les bornes décrivent où peut aller le <em>centre</em> de la pastille : la
/// zone de travail rétrécie d'une demi-pastille de chaque côté.
/// </para>
/// </summary>
public sealed class NotchBody
{
    /// <summary>Part de vitesse gardée après une seconde de glisse (0,985 par image à 60 i/s).</summary>
    public const double FrictionPerSecond = 0.40;

    /// <summary>Part de vitesse rendue par un choc contre un bord.</summary>
    public const double Restitution = 0.72;

    /// <summary>Vitesse, en DIP/s, sous laquelle le vol s'arrête.</summary>
    public const double RestSpeed = 140;

    /// <summary>Durée maximale d'un vol, en secondes : au-delà, l'atterrissage reprend.</summary>
    public const double MaxFlightSeconds = 1.6;

    /// <summary>Écrasement maximal à l'impact (0,12 : la pastille perd 12 % dans l'axe du choc).</summary>
    public const double MaxSquash = 0.12;

    /// <summary>Vitesse d'impact, en DIP/s, qui donne l'écrasement maximal.</summary>
    public const double SquashSpeed = 1800;

    /// <summary>Temps de retour de l'écrasement, en secondes.</summary>
    public const double SquashDecaySeconds = 0.09;

    private readonly ScreenRect _bounds;
    private double _squashX;
    private double _squashY;

    public NotchBody(double x, double y, double velocityX, double velocityY, ScreenRect bounds)
    {
        _bounds = bounds;
        X = Math.Clamp(x, bounds.X, Math.Max(bounds.X, bounds.Right));
        Y = Math.Clamp(y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom));
        VelocityX = velocityX;
        VelocityY = velocityY;
    }

    public double X { get; private set; }

    public double Y { get; private set; }

    public double VelocityX { get; private set; }

    public double VelocityY { get; private set; }

    /// <summary>Nombre de chocs depuis le lâcher.</summary>
    public int Bounces { get; private set; }

    /// <summary>Temps de vol écoulé, en secondes.</summary>
    public double Elapsed { get; private set; }

    public double Speed => Math.Sqrt((VelocityX * VelocityX) + (VelocityY * VelocityY));

    /// <summary>Vrai quand l'élan est épuisé : l'atterrissage peut reprendre.</summary>
    public bool IsSpent => Speed < RestSpeed || Elapsed >= MaxFlightSeconds;

    /// <summary>
    /// Facteurs d'échelle de l'écrasement : un choc contre un côté aplatit la
    /// largeur et allonge la hauteur, un choc en haut ou en bas l'inverse.
    /// </summary>
    public (double X, double Y) Squash => (1 - _squashX + _squashY * 0.6, 1 - _squashY + _squashX * 0.6);

    /// <summary>Avance le vol de <paramref name="dt"/> secondes.</summary>
    public void Step(double dt)
    {
        if (dt <= 0 || double.IsNaN(dt))
        {
            return;
        }

        // Pas fins : un pas de plus de 16 ms à 3 000 DIP/s traverserait le bord.
        int steps = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 240)));
        double h = dt / steps;

        for (int i = 0; i < steps; i++)
        {
            Advance(h);
        }

        Elapsed += dt;

        double decay = Math.Exp(-dt / SquashDecaySeconds);
        _squashX *= decay;
        _squashY *= decay;
    }

    private void Advance(double h)
    {
        double keep = Math.Pow(FrictionPerSecond, h);
        VelocityX *= keep;
        VelocityY *= keep;
        X += VelocityX * h;
        Y += VelocityY * h;

        if (X < _bounds.X || X > _bounds.Right)
        {
            X = Math.Clamp(X, _bounds.X, _bounds.Right);
            _squashX = Math.Max(_squashX, SquashFor(VelocityX));
            VelocityX = -VelocityX * Restitution;
            Bounces++;
        }

        if (Y < _bounds.Y || Y > _bounds.Bottom)
        {
            Y = Math.Clamp(Y, _bounds.Y, _bounds.Bottom);
            _squashY = Math.Max(_squashY, SquashFor(VelocityY));
            VelocityY = -VelocityY * Restitution;
            Bounces++;
        }
    }

    private static double SquashFor(double speed) => MaxSquash * Math.Min(1, Math.Abs(speed) / SquashSpeed);
}
