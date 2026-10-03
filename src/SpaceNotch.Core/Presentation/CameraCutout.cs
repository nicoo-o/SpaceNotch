using System;
using SpaceNotch.Core.Scenes;

namespace SpaceNotch.Core.Presentation;

/// <summary>
/// Encoche de la caméra (phase C) : sur un portable dont l'écran est échancré
/// pour la caméra, la notch au repos ne doit jamais être plus étroite ni moins
/// haute que l'échancrure — sinon on voit l'encoche noire dépasser de part et
/// d'autre de la forme. Le réglage était écrit mais jamais lu.
/// </summary>
public static class CameraCutout
{
    /// <summary>Largeur d'une échancrure ordinaire, en DIP.</summary>
    public const double DefaultWidth = 200;

    /// <summary>Hauteur couverte au minimum, en DIP.</summary>
    public const double Height = 30;

    /// <summary>Bornes de la largeur personnalisée, en DIP.</summary>
    public const double MinimumWidth = 80;

    public const double MaximumWidth = 400;

    /// <summary>Largeur à couvrir : celle choisie si elle est personnalisée, sinon la largeur ordinaire.</summary>
    public static double WidthFor(bool custom, double customWidth)
        => custom && double.IsFinite(customWidth) && customWidth > 0
            ? Math.Clamp(customWidth, MinimumWidth, MaximumWidth)
            : DefaultWidth;

    /// <summary>La forme au repos agrandie juste assez pour couvrir l'échancrure.</summary>
    public static IslandFootprint Cover(IslandFootprint rest, double width)
    {
        if (!rest.IsValid || !(width > 0))
        {
            return rest;
        }

        return new IslandFootprint(Math.Max(rest.Width, width), Math.Max(rest.Height, Height));
    }
}
