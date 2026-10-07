using System.Collections.Generic;
using System.Globalization;
using SpaceNotch.Core.Localization;

namespace SpaceNotch.Features.Menu;

/// <summary>Une tuile du tableau de bord : son icône, son nom, et la commande du menu rapide qu'elle lance.</summary>
/// <param name="IconKey">Clé de glyphe (GlyphCatalog, PixelGlyphs).</param>
/// <param name="Label">Nom affiché et lu par Narrateur.</param>
/// <param name="ActionId">Commande du menu rapide (<see cref="QuickMenuFeature"/>).</param>
/// <param name="Value">Valeur de la commande, comme la ligne du menu.</param>
public sealed record QuickMenuTile(string IconKey, string Label, string ActionId, string? Value = null);

/// <summary>
/// Les quatre tuiles qui suivent la recherche au clic sur le repos (ADR-028).
/// Elles mènent aux mêmes commandes que le menu rapide, qui reste le raccourci
/// du clic droit. Cette liste est la source des tuiles ; les lignes du menu ont
/// encore la leur (libellés et icônes à garder alignés).
/// </summary>
public static class QuickMenuTiles
{
    /// <summary>Quatre places, pas plus : au-delà, le tableau redevient un menu.</summary>
    public const int Max = 4;

    /// <summary>Commande levée par la scène de recherche ; sa valeur est la position de la tuile.</summary>
    public const string TileAction = "launcher.tile";

    /// <summary>Les tuiles, dans l'ordre d'affichage.</summary>
    public static IReadOnlyList<QuickMenuTile> All() =>
    [
        new("Timer", Lang.T("Minuteur", "Timer"), QuickMenuFeature.TimerAction, "15"),
        new("Clipboard", Lang.T("Presse-papier", "Clipboard"), QuickMenuFeature.ClipboardAction),
        new("Menu", Lang.T("Note", "Note"), QuickMenuFeature.NoteAction),
        new("Command", Lang.T("Plus", "More"), QuickMenuFeature.MoreAction)
    ];

    /// <summary>
    /// La tuile d'une commande, ou <c>null</c> : le menu rapide en tire le nom et
    /// l'icône de ses lignes communes, au lieu de les recopier (n° 50).
    /// </summary>
    public static QuickMenuTile? For(string actionId)
    {
        foreach (QuickMenuTile tile in All())
        {
            if (tile.ActionId == actionId)
            {
                return tile;
            }
        }

        return null;
    }

    /// <summary>La tuile à cette position (valeur de <see cref="TileAction"/>), ou <c>null</c>.</summary>
    public static QuickMenuTile? At(string? position)
    {
        IReadOnlyList<QuickMenuTile> tiles = All();

        return int.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < tiles.Count
            ? tiles[index]
            : null;
    }
}
