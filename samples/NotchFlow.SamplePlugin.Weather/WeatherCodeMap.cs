namespace NotchFlow.SamplePlugin.Weather;

/// <summary>
/// Traduction des codes WMO — le standard utilisé par les services météo — en
/// libellé français et en glyphe.
///
/// Les codes de glyphe proviennent de la table officielle Segoe Fluent Icons. Ils
/// sont écrits sous forme de caractère littéral plutôt que choisis dans le
/// répertoire de clés logiques de l'hôte : c'est exactement ce que la scène
/// générique accepte, et cela évite qu'un greffon apportant un domaine inconnu de
/// l'hôte doive attendre qu'une clé soit ajoutée pour lui.
///
/// <b>Il n'existe aucun glyphe de pluie dans Segoe Fluent Icons.</b> Les codes
/// pluvieux retombent donc sur le nuage, faute de mieux — le dire ici vaut mieux
/// que de laisser croire à une approximation involontaire.
/// </summary>
public static class WeatherCodeMap
{
    /// <summary>Ciel dégagé, de jour. Glyphe « Brightness » (U+E706).</summary>
    private const string SunGlyph = "\uE706";

    /// <summary>Ciel dégagé, de nuit. Glyphe « QuietHours » (U+E708).</summary>
    private const string MoonGlyph = "\uE708";

    /// <summary>Nuage (U+E753), utilisé pour tout ce qui est couvert ou pluvieux.</summary>
    private const string CloudGlyph = "\uE753";

    /// <summary>Neige ou gel. Glyphe « Frigid » (U+E9CA).</summary>
    private const string SnowGlyph = "\uE9CA";

    /// <summary>Orage. Glyphe « LightningBolt » (U+E945).</summary>
    private const string StormGlyph = "\uE945";

    /// <summary>Libellé lisible d'un code WMO.</summary>
    public static string Describe(int code) => code switch
    {
        0 => "Ciel dégagé",
        1 => "Plutôt dégagé",
        2 => "Partiellement nuageux",
        3 => "Couvert",
        45 or 48 => "Brouillard",
        51 or 53 or 55 => "Bruine",
        56 or 57 => "Bruine verglaçante",
        61 or 63 or 65 => "Pluie",
        66 or 67 => "Pluie verglaçante",
        71 or 73 or 75 => "Neige",
        77 => "Grains de neige",
        80 or 81 or 82 => "Averses",
        85 or 86 => "Averses de neige",
        95 => "Orage",
        96 or 99 => "Orage avec grêle",
        _ => "Condition inconnue"
    };

    /// <summary>
    /// Glyphe correspondant à une condition. <paramref name="isDay"/> distingue le
    /// soleil de la lune pour un ciel dégagé, seule condition où la différence
    /// change réellement ce qu'on voit.
    /// </summary>
    public static string GlyphFor(int code, bool isDay) => code switch
    {
        0 or 1 => isDay ? SunGlyph : MoonGlyph,
        71 or 73 or 75 or 77 or 85 or 86 => SnowGlyph,
        95 or 96 or 99 => StormGlyph,
        _ => CloudGlyph
    };
}
