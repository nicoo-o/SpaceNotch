using System;

namespace SpaceNotch.Core.Animation;

/// <summary>
/// Résolveur analytique pour oscillateur harmonique amorti (Spring Physics).
/// </summary>
public sealed class SpringSolver
{
    private readonly SpringParameters _parameters;
    private readonly double _omega0;
    private readonly double _zeta;
    private readonly double _omegaD;

    public SpringSolver(SpringParameters parameters)
    {
        _parameters = parameters;
        double k = Math.Max(0.1, parameters.Stiffness);
        double m = Math.Max(0.1, parameters.Mass);
        double c = Math.Max(0.0, parameters.Damping);

        _omega0 = Math.Sqrt(k / m);
        _zeta = c / (2.0 * Math.Sqrt(m * k));

        if (_zeta < 1.0)
        {
            _omegaD = _omega0 * Math.Sqrt(1.0 - _zeta * _zeta);
        }
        else if (_zeta > 1.0)
        {
            _omegaD = _omega0 * Math.Sqrt(_zeta * _zeta - 1.0);
        }
        else
        {
            _omegaD = 0.0;
        }
    }

    /// <summary>
    /// Calcule la valeur à l'instant t (en secondes) entre startValue et targetValue.
    /// </summary>
    public (double Value, double Velocity) Evaluate(double t, double startValue, double targetValue, double initialVelocity = 0.0)
    {
        if (t <= 0)
        {
            return (startValue, initialVelocity);
        }

        double x0 = startValue - targetValue;
        double v0 = initialVelocity;

        double x;
        double v;

        if (_zeta < 1.0)
        {
            // Sous-amorti (oscillation douce avec léger rebond naturel)
            double decay = Math.Exp(-_zeta * _omega0 * t);
            double cos = Math.Cos(_omegaD * t);
            double sin = Math.Sin(_omegaD * t);

            double c1 = x0;
            double c2 = (v0 + _zeta * _omega0 * x0) / _omegaD;

            x = decay * (c1 * cos + c2 * sin);
            v = -_zeta * _omega0 * x + decay * (-c1 * _omegaD * sin + c2 * _omegaD * cos);
        }
        else if (Math.Abs(_zeta - 1.0) < 1e-6)
        {
            // Amorti critique (retour au repos le plus rapide sans oscillation)
            double decay = Math.Exp(-_omega0 * t);
            double c1 = x0;
            double c2 = v0 + _omega0 * x0;

            x = decay * (c1 + c2 * t);
            v = decay * (c2 - _omega0 * (c1 + c2 * t));
        }
        else
        {
            // Sur-amorti
            double r1 = -_omega0 * (_zeta - Math.Sqrt(_zeta * _zeta - 1.0));
            double r2 = -_omega0 * (_zeta + Math.Sqrt(_zeta * _zeta - 1.0));

            double c2 = (v0 - r1 * x0) / (r2 - r1);
            double c1 = x0 - c2;

            double e1 = Math.Exp(r1 * t);
            double e2 = Math.Exp(r2 * t);

            x = c1 * e1 + c2 * e2;
            v = c1 * r1 * e1 + c2 * r2 * e2;
        }

        return (targetValue + x, v);
    }

    /// <summary>
    /// Détermine si le ressort est stabilisé (repos).
    /// </summary>
    public bool HasSettled(double t, double startValue, double targetValue, double threshold = 0.5)
    {
        var (value, velocity) = Evaluate(t, startValue, targetValue);
        return Math.Abs(value - targetValue) < threshold && Math.Abs(velocity) < threshold;
    }
}
