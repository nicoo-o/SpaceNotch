using System.Collections.Generic;

namespace NotchFlow_App.Views;

/// <summary>
/// Correspondance entre une clé d'icône logique et un glyphe de la police
/// d'icônes Windows.
///
/// <para>
/// Une fonctionnalité déclare une clé — « Timer », « Bluetooth » — et jamais un
/// code de glyphe. C'est ce qui rend le jeu d'icônes remplaçable sans toucher au
/// moindre producteur d'activités, et c'est aussi ce qui permet au glyphe de tête
/// de la carte fermée de montrer *de quoi* il s'agit sans qu'aucun code de rendu
/// ne connaisse le domaine concerné.
/// </para>
///
/// <para>
/// Deux formes restent acceptées, et la seconde est le contrat d'extensibilité :
/// une clé du répertoire, ou un glyphe littéral — un unique caractère de la zone
/// à usage privé. Un greffon qui apporte un domaine inconnu de l'hôte n'a donc
/// pas à attendre qu'on ajoute une clé pour lui. Voir ADR-010.
/// </para>
///
/// <para>
/// Ce répertoire était auparavant dupliqué dans la scène générique et dans la
/// scène de retour système, avec des valeurs de repli différentes. Deux tables
/// divergentes pour la même clé produisaient deux glyphes différents selon la
/// scène ouverte — exactement le genre d'incohérence qu'aucun test ne
/// remarquerait. Il n'en existe plus qu'une.
/// </para>
/// </summary>
internal static class GlyphCatalog
{
    /// <summary>Glyphe de repli lorsqu'aucune clé utilisable n'est fournie.</summary>
    public const string FallbackKey = "Info";

    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["Music"] = "\uE8D6",
        ["Volume"] = "\uE995",
        ["VolumeMute"] = "\uE74F",
        ["VolumeLow"] = "\uE992",
        ["VolumeMedium"] = "\uE993",
        ["VolumeHigh"] = "\uE995",
        ["Brightness"] = "\uE706",
        ["Bluetooth"] = "\uE702",
        ["Notification"] = "\uEA8F",
        ["Timer"] = "\uE916",
        ["Folder"] = "\uE8B7",
        ["Clipboard"] = "\uE77F",
        ["Launcher"] = "\uE71D",
        ["Info"] = "\uE946"
    };

    /// <summary>
    /// Résout une clé en glyphe. Un caractère isolé est pris pour un glyphe, pas
    /// pour une clé : c'est ce qui permet à un greffon d'apporter sa propre
    /// icône sans qu'on l'ait prévue.
    /// </summary>
    public static string Resolve(string? iconKey, string fallbackKey = FallbackKey)
    {
        if (string.IsNullOrEmpty(iconKey))
        {
            return For(fallbackKey);
        }

        if (Glyphs.TryGetValue(iconKey, out string? glyph))
        {
            return glyph;
        }

        return iconKey.Length == 1 ? iconKey : For(fallbackKey);
    }

    private static string For(string key)
        => Glyphs.TryGetValue(key, out string? glyph) ? glyph : Glyphs[FallbackKey];
}
