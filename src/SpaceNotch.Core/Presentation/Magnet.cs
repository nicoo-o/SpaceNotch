using System;

namespace SpaceNotch.Core.Presentation;

/// <summary>Attraction de la notch vers le curseur : décalage (DIPs) et échelle.</summary>
public readonly record struct MagnetPull(double DX, double DY, double Scale)
{
    public static MagnetPull None => new(0, 0, 1);
}

/// <summary>
/// Notch magnétique (U4) : à moins de 120 DIPs, la notch se penche de quelques
/// DIPs vers le curseur et grossit à peine. Au-delà, rien.
/// </summary>
public static class Magnet
{
    /// <summary>Portée de l'attraction, en DIPs depuis le bord de la notch.</summary>
    public const double Reach = 120;

    /// <summary>Décalage maximal, en DIPs.</summary>
    public const double MaxShift = 4;

    /// <summary>Grossissement maximal.</summary>
    public const double MaxGrow = 0.015;

    public static MagnetPull For(double cursorX, double cursorY, ScreenRect notch)
    {
        // Distance au rectangle, pas au centre : une notch large attire autant
        // par ses bords que par son milieu.
        double dx = cursorX < notch.X ? cursorX - notch.X : cursorX > notch.Right ? cursorX - notch.Right : 0;
        double dy = cursorY < notch.Y ? cursorY - notch.Y : cursorY > notch.Bottom ? cursorY - notch.Bottom : 0;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));

        // Dessus : c'est le survol qui prend le relais ; trop loin : rien.
        if (distance <= 0 || distance >= Reach)
        {
            return MagnetPull.None;
        }

        double strength = 1 - (distance / Reach);
        strength *= strength;

        double towardX = cursorX - notch.CenterX, towardY = cursorY - notch.CenterY;
        double length = Math.Sqrt((towardX * towardX) + (towardY * towardY));

        if (length < 1e-6)
        {
            return MagnetPull.None;
        }

        return new MagnetPull(
            towardX / length * MaxShift * strength,
            towardY / length * MaxShift * strength,
            1 + (MaxGrow * strength));
    }
}
