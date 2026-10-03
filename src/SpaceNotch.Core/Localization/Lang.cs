using System;
using System.Globalization;

namespace SpaceNotch.Core.Localization;

/// <summary>
/// Langue de l'interface : celle de Windows. Français si Windows est en
/// français, anglais sinon — et jamais les deux à la fois.
///
/// <para>
/// Chaque texte visible s'écrit dans les deux langues, côte à côte, à l'endroit
/// où il sert : <c>Lang.T("Connecté", "Connected")</c> en code,
/// <c>{l:Loc Fr='Connecté', En='Connected'}</c> en XAML. Pas de fichier de
/// ressources à tenir synchronisé, pas de clé orpheline : une phrase qui manque
/// dans une langue ne compile pas.
/// </para>
/// </summary>
public static class Lang
{
    private static bool? _french;

    /// <summary>Vrai lorsque l'interface parle français.</summary>
    public static bool French
    {
        get => _french ??= IsFrench(CultureInfo.CurrentUICulture);
        set => _french = value;
    }

    /// <summary>Le texte dans la langue de l'interface.</summary>
    public static string T(string french, string english)
    {
        ArgumentNullException.ThrowIfNull(french);
        ArgumentNullException.ThrowIfNull(english);

        return French ? french : english;
    }

    /// <summary>
    /// Un nombre et son nom accordé : « 1 notification retenue », « 3 notifications
    /// retenues ». En français, 0 et 1 sont au singulier ; en anglais, seul 1 l'est.
    /// Remplace les « notification(s) » qui se lisent comme un formulaire.
    /// </summary>
    public static string Count(int count, string frenchOne, string frenchMany, string englishOne, string englishMany)
    {
        bool one = French ? Math.Abs(count) < 2 : Math.Abs(count) == 1;
        string noun = T(one ? frenchOne : frenchMany, one ? englishOne : englishMany);
        return count.ToString(CultureInfo.CurrentCulture) + " " + noun;
    }

    /// <summary>Vrai pour toute variante du français (fr-FR, fr-BE, fr-CA…).</summary>
    public static bool IsFrench(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TwoLetterISOLanguageName == "fr";
    }
}
