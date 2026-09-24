using System;

namespace SpaceNotch.Core.Presentation;

/// <summary>
/// Éléments qui se déplacent d'une présentation à l'autre au lieu de
/// disparaître et de réapparaître.
/// </summary>
public enum MorphAnchorKind
{
    Icon = 0,
    Artwork = 1,
    Title = 2,
    Subtitle = 3,
    Progress = 4,
    PrimaryAction = 5
}

/// <summary>Un rectangle, en DIPs, dans un repère commun aux deux présentations.</summary>
public readonly record struct MorphRect(double X, double Y, double Width, double Height)
{
    public double CenterY => Y + (Height / 2);

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Transformation qui, appliquée à l'élément d'arrivée, le fait coïncider avec
/// l'élément de départ. Animée vers l'identité, elle donne le morphing.
/// </summary>
/// <param name="TranslateX">Décalage horizontal, en DIPs.</param>
/// <param name="TranslateY">Décalage vertical, en DIPs.</param>
/// <param name="ScaleX">Échelle horizontale, autour du coin supérieur gauche.</param>
/// <param name="ScaleY">Échelle verticale, autour du coin supérieur gauche.</param>
public readonly record struct MorphTransform(double TranslateX, double TranslateY, double ScaleX, double ScaleY)
{
    public static MorphTransform Identity => new(0, 0, 1, 1);

    public bool IsIdentity => this == Identity;

    /// <summary>
    /// Principe « FLIP » : on connaît où l'élément <em>était</em> et où il
    /// <em>arrive</em> ; on le place d'abord à l'ancienne position, puis on laisse
    /// la transformation revenir à l'identité. Le compositeur n'anime ainsi
    /// qu'une translation et une échelle, jamais la mise en page.
    /// </summary>
    /// <param name="from">Position de départ — l'élément de la forme compacte.</param>
    /// <param name="to">Position d'arrivée — l'élément de la forme ouverte.</param>
    /// <param name="uniform">
    /// Vrai pour un texte : l'échelle est la même dans les deux sens, réglée sur
    /// la hauteur, pour ne jamais déformer les lettres ; les deux éléments sont
    /// alignés sur leur bord gauche et leur centre vertical.
    /// </param>
    public static MorphTransform Between(MorphRect from, MorphRect to, bool uniform)
    {
        if (from.IsEmpty || to.IsEmpty)
        {
            return Identity;
        }

        if (!uniform)
        {
            return new MorphTransform(
                from.X - to.X,
                from.Y - to.Y,
                from.Width / to.Width,
                from.Height / to.Height);
        }

        double scale = Math.Clamp(from.Height / to.Height, 0.25, 4);
        double scaledHeight = to.Height * scale;

        return new MorphTransform(
            from.X - to.X,
            from.CenterY - (to.Y + (scaledHeight / 2)),
            scale,
            scale);
    }
}
