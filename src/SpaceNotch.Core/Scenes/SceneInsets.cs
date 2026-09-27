namespace SpaceNotch.Core.Scenes;

/// <summary>
/// La marge noire entre le bord de la notch et son contenu : la même pour
/// toutes les scènes ouvertes, pour qu'aucune ne paraisse plus serrée qu'une
/// autre.
///
/// <para>
/// 22 sur les flancs, 12 en haut (le haut est le bord de l'écran, il ne se
/// voit pas), 18 en bas. Avec des congés de 34, un contenu posé dans l'angle
/// du bas reste à plus de 12 DIPs de la courbe : le contenu suit la forme
/// sans jamais la frôler. Le jeton XAML <c>NfScenePadding</c> porte les mêmes
/// valeurs ; un test vérifie qu'ils ne divergent pas.
/// </para>
/// </summary>
public static class SceneInsets
{
    /// <summary>Marge des flancs, en DIPs.</summary>
    public const double Side = 22;

    /// <summary>Marge du haut, en DIPs.</summary>
    public const double Top = 12;

    /// <summary>Marge du bas, en DIPs.</summary>
    public const double Bottom = 18;

    /// <summary>Marge intérieure d'une forme compacte (pastille), de chaque côté.</summary>
    public const double Compact = 16;

    /// <summary>
    /// Encombrement d'une scène dont le contenu mesure
    /// <paramref name="contentWidth"/> × <paramref name="contentHeight"/> :
    /// le contenu, ses marges et, si la scène est accrochée, ses deux épaules.
    /// </summary>
    public static IslandFootprint Wrap(double contentWidth, double contentHeight, bool shoulders = true)
        => new(
            contentWidth + (2 * Side) + (shoulders ? 2 * NotchGeometry.DefaultShoulder : 0),
            contentHeight + Top + Bottom);
}
