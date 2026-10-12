namespace SpaceNotch.Core.Motion;

/// <summary>
/// Intensité de Pixel (direction §7, point 9) : « vivant sans limite », mais
/// réglable. Normal est le Pixel par défaut ; le mouvement réduit de Windows
/// le rend Calme quel que soit ce réglage (ADR-029).
/// </summary>
public enum PixelIntensity
{
    /// <summary>Immobile, yeux ouverts au centre ; il garde ses humeurs (n° 22).</summary>
    Calm,

    /// <summary>Il suit le curseur, cligne, jette un coup d'œil et bâille le soir.</summary>
    Normal,

    /// <summary>Plus vivant : il regarde autour de lui quand le pointeur ne bouge pas, et bâille plus souvent.</summary>
    Expressive
}
