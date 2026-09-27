using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Presentation;

/// <summary>Place d'une carte dans l'éventail : décalage horizontal (DIPs), rotation (degrés), échelle.</summary>
public readonly record struct FanSlot(double Offset, double Rotation, double Scale);

/// <summary>
/// Notifications en éventail (S1) : empilées, les cartes se recouvrent ; au
/// survol, elles s'écartent vers la droite de 58 DIPs chacune avec une
/// rotation alternée de ±1,5°, comme une main de cartes. À l'horizontale : la
/// notch est large et basse, une main de cartes s'y ouvre de côté.
/// </summary>
public static class CardFan
{
    /// <summary>Écart entre deux cartes ouvertes, en DIPs.</summary>
    public const double Spread = 58;

    /// <summary>Recouvrement des cartes empilées : chacune dépasse de 6 DIPs.</summary>
    public const double Peek = 6;

    /// <summary>Cartes affichées au plus ; les suivantes restent dans le compteur.</summary>
    public const int MaxCards = 3;

    public static IReadOnlyList<FanSlot> Layout(int count, bool open)
    {
        int n = Math.Clamp(count, 0, MaxCards);
        var slots = new FanSlot[n];

        for (int i = 0; i < n; i++)
        {
            slots[i] = open
                ? new FanSlot(i * Spread, i == 0 ? 0 : (i % 2 == 1 ? 1.5 : -1.5), 1)
                : new FanSlot(i * Peek, 0, 1 - (i * 0.04));
        }

        return slots;
    }

    /// <summary>Largeur totale occupée par l'éventail, carte de <paramref name="cardWidth"/> DIPs.</summary>
    public static double Width(int count, bool open, double cardWidth)
    {
        int n = Math.Clamp(count, 0, MaxCards);
        return n == 0 ? 0 : cardWidth + ((n - 1) * (open ? Spread : Peek));
    }

    /// <summary>Hauteur d'une carte de l'éventail, en DIPs.</summary>
    public const double CardHeight = 36;
}
