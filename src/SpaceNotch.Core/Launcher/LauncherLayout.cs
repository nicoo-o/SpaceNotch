using SpaceNotch.Core.Scenes;

namespace SpaceNotch.Core.Launcher;

/// <summary>
/// Dimensions de la recherche ouverte, en DIPs. La notch prend la hauteur de
/// ce qu'elle montre : un résultat, une ligne ; dix résultats, dix lignes — et
/// au-delà d'un plafond, la liste défile.
/// </summary>
public static class LauncherLayout
{
    /// <summary>Largeur ouverte, épaules comprises.</summary>
    public const double Width = 600;

    public const double SearchBar = 52;
    public const double Separator = 1;
    public const double ListTop = 6;
    public const double SectionHeader = 26;
    public const double Row = 40;
    public const double CalculationRow = 70;
    public const double Footer = 40;
    public const double Bottom = 10;

    /// <summary>Hauteur maximale : au-delà, la liste défile.</summary>
    public const double MaxHeight = 470;

    /// <summary>Hauteur quand il n'y a encore rien à montrer : le champ, une ligne d'aide, le pied.</summary>
    public const double EmptyHeight = SearchBar + Separator + ListTop + Row + Footer + Bottom;

    public static double HeightFor(IReadOnlyList<LauncherSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        if (sections.Count == 0)
        {
            return EmptyHeight;
        }

        double list = ListTop;

        foreach (LauncherSection section in sections)
        {
            list += SectionHeader;

            foreach (LauncherResult item in section.Items)
            {
                list += item.Kind == LauncherResultKind.Calculation ? CalculationRow : Row;
            }
        }

        return Math.Min(MaxHeight, SearchBar + Separator + list + Footer + Bottom);
    }

    /// <summary>
    /// Hauteur minimale quand le panneau d'actions (Ctrl+K) est ouvert : il
    /// flotte au-dessus de la liste, et une liste d'un seul résultat serait trop
    /// courte pour le contenir.
    /// </summary>
    public const double ActionsPanelMinHeight = 380;

    public static IslandFootprint FootprintFor(IReadOnlyList<LauncherSection> sections, bool actionsOpen = false)
    {
        double height = HeightFor(sections);

        if (actionsOpen)
        {
            height = Math.Max(height, ActionsPanelMinHeight);
        }

        return new IslandFootprint(Width, Math.Round(height));
    }
}
