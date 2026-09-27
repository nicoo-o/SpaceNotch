namespace SpaceNotch.Core.Menu;

/// <summary>
/// Dimensions du menu rapide, en DIPs : en-tête 40, lignes de 36, filets de 9,
/// marges de la notch (<see cref="SpaceNotch.Core.Scenes.SceneInsets"/>). Déplier « Accrocher à… » ajoute la rangée des trois bords.
/// </summary>
public static class QuickMenuLayout
{
    public const double Width = 336 + (2 * SpaceNotch.Core.Scenes.SceneInsets.Side) + (2 * SpaceNotch.Core.Scenes.NotchGeometry.DefaultShoulder);
    public const double Header = 40;
    public const double Row = 36;
    public const double Separator = 9;
    public const double DockChoices = 38;
    public const double Top = SpaceNotch.Core.Scenes.SceneInsets.Top;
    public const double Bottom = SpaceNotch.Core.Scenes.SceneInsets.Bottom;

    /// <summary>Rechercher, Minuteur, Note, Presse-papier, Étagère · Détacher, Accrocher · Réglages, Quitter.</summary>
    public const int Rows = 9;

    public const int Separators = 2;

    public const double Height = Top + Header + (Rows * Row) + (Separators * Separator) + Bottom;

    public static double HeightFor(bool dockExpanded) => Height + (dockExpanded ? DockChoices : 0);
}
