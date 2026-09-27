using System;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Fader de volume cranté (S4) : onze graduations, une tous les 10 %. Franchir
/// un cran l'allume et fait rebondir le curseur ; la molette avance de 2 %.
/// </summary>
public static class VolumeFader
{
    /// <summary>Nombre de graduations (0, 10, … 100 %).</summary>
    public const int Ticks = 11;

    /// <summary>Pas de la molette, en fraction.</summary>
    public const double WheelStep = 0.02;

    /// <summary>Durée du micro-rebond par cran, en secondes.</summary>
    public const double BounceSeconds = 0.08;

    /// <summary>Graduations allumées pour un niveau (0..1) : toutes celles atteintes.</summary>
    public static int LitTicks(double level) => (int)Math.Floor((Math.Clamp(level, 0, 1) * 10) + 1e-9) + 1;

    /// <summary>Nombre de crans franchis entre deux niveaux, dans un sens ou l'autre.</summary>
    public static int Crossed(double before, double after) => Math.Abs(LitTicks(after) - LitTicks(before));

    /// <summary>
    /// Niveau après un cran de molette (<paramref name="notches"/> : +1 vers le
    /// haut, −1 vers le bas ; une molette fine envoie des fractions).
    /// </summary>
    public static double Wheel(double level, double notches)
        => Math.Round(Math.Clamp(level + (notches * WheelStep), 0, 1), 4);
}
