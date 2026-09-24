using System;

namespace SpaceNotch.Core.Scenes;

/// <summary>
/// Mode géométrique de l'Island.
///
/// <para>
/// <b>Il n'en existe qu'un, et c'est délibéré.</b> SpaceNotch est une notch
/// attachée au bord supérieur de l'écran, jamais une capsule flottante. L'enum
/// existe pour que la contrainte soit <em>nommée</em> dans le code — un
/// développement futur qui voudrait une forme flottante devra ajouter une valeur
/// ici, casser le test qui vérifie qu'il n'y en a qu'une, et donc relire
/// ADR-017. C'est la façon la plus sûre d'empêcher qu'une refonte transforme par
/// accident la notch en widget.
/// </para>
/// </summary>
public enum IslandGeometryMode
{
    /// <summary>
    /// Collée au bord supérieur : bord haut sur l'écran d'un bout à l'autre,
    /// épaules concaves, grands congés en bas. La seule forme de l'UI principale.
    /// </summary>
    TopAttached = 0
}

/// <summary>
/// Paramètres de la silhouette TopAttached, et règles qui en dérivent le rayon
/// effectivement tracé.
///
/// <para>
/// <b>Deux rayons, une seule courbe de passage.</b> Le rayon n'est plus unique :
/// une forme compacte et une forme ouverte n'ont pas les mêmes proportions, et
/// un rayon unique était soit trop sage ouvert, soit impossible fermé. Le rayon
/// est donc interpolé <em>continûment</em> selon la hauteur de la forme, et non
/// selon un état : pendant le morphing, il suit la hauteur image par image, si
/// bien qu'aucun saut de courbure n'est visible. C'est la même règle que la
/// dissolution — elle naît de la croissance, pas d'un basculement. Voir ADR-017.
/// </para>
/// </summary>
/// <param name="CompactRadius">Rayon des congés à la hauteur compacte, en DIPs.</param>
/// <param name="ExpandedRadius">Rayon des congés d'une forme pleinement ouverte, en DIPs.</param>
/// <param name="Shoulder">Rayon des épaules concaves au bord de l'écran, en DIPs.</param>
/// <param name="Smoothing">Exposant de la superellipse. Voir <see cref="IslandShape"/>.</param>
public readonly record struct NotchGeometry(
    double CompactRadius,
    double ExpandedRadius,
    double Shoulder,
    double Smoothing)
{
    /// <summary>Rayon compact de référence : ~26, dans la fourchette 26–32 du plan.</summary>
    public const double DefaultCompactRadius = 26;

    /// <summary>Rayon ouvert de référence : ~34, dans la fourchette 30–40 du plan.</summary>
    public const double DefaultExpandedRadius = 34;

    /// <summary>
    /// Épaule de référence. Mesurée sur la vidéo de référence : l'épaule y vaut
    /// environ les deux tiers du congé du bas. Assez pour que le bord de l'écran
    /// « coule » dans la notch, assez peu pour ne pas dessiner un entonnoir.
    /// </summary>
    public const double DefaultShoulder = 12;

    /// <summary>Hauteur à partir de laquelle le rayon quitte sa valeur compacte, en DIPs.</summary>
    public const double RadiusOnsetHeight = 34;

    /// <summary>Hauteur à laquelle le rayon atteint sa valeur ouverte, en DIPs.</summary>
    public const double RadiusFullHeight = 120;

    /// <summary>Le seul mode de l'UI principale.</summary>
    public static IslandGeometryMode Mode => IslandGeometryMode.TopAttached;

    /// <summary>Géométrie de référence.</summary>
    public static NotchGeometry Default => new(
        DefaultCompactRadius,
        DefaultExpandedRadius,
        DefaultShoulder,
        IslandShape.Squircle);

    /// <summary>
    /// Rayon tracé pour un encombrement : interpolé selon la hauteur, puis borné
    /// par ce que la forme peut physiquement porter.
    /// </summary>
    public double RadiusFor(IslandFootprint footprint)
    {
        double progress = Progress(footprint.Height);
        double radius = CompactRadius + ((ExpandedRadius - CompactRadius) * progress);

        return IslandShape.EffectiveRadius(footprint.Width, footprint.Height, radius, Shoulder);
    }

    /// <summary>Épaule tracée pour un encombrement.</summary>
    public double ShoulderFor(IslandFootprint footprint)
        => IslandShape.EffectiveShoulder(footprint.Width, footprint.Height, Shoulder);

    /// <summary>Contour complet pour un encombrement.</summary>
    public ShapePoint[] Silhouette(IslandFootprint footprint, double band = 0)
        => IslandShape.Silhouette(
            footprint.Width,
            footprint.Height,
            RadiusFor(footprint),
            Smoothing,
            band,
            Shoulder);

    /// <summary>
    /// Avancement du rayon entre ses deux valeurs, lissé aux extrémités : une
    /// interpolation linéaire produirait un coude perceptible au moment où la
    /// hauteur franchit le seuil de départ.
    /// </summary>
    private static double Progress(double height)
    {
        double linear = Math.Clamp(
            (height - RadiusOnsetHeight) / (RadiusFullHeight - RadiusOnsetHeight),
            0.0,
            1.0);

        return linear * linear * (3 - (2 * linear));
    }
}
