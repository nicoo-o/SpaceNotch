using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.State;
using Windows.UI;

namespace SpaceNotch_App.Views;

/// <summary>
/// Teinte associée à chaque état d'activité.
///
/// <para>
/// La teinte du glyphe de tête vient de l'état, jamais de l'application source :
/// un lecteur de musique et un téléchargement ne se distinguent pas par la
/// couleur de leur marque mais par la nature de ce qu'ils font. C'est ce qui
/// permet à une même forme de dire « ceci progresse » en ambre et « ceci demande
/// votre attention » en rouge sans que la géométrie change d'un pixel.
/// </para>
///
/// <para>
/// La teinte n'est jamais le seul porteur de l'information : la légende de la
/// carte nomme l'état en mots. Une couleur seule serait perdue en contraste
/// élevé, pour une partie des daltonismes, et pour quiconque confond deux teintes
/// voisines. Voir ADR-014.
/// </para>
///
/// <para>
/// Les couleurs sont lues dans les jetons de thème par leur clé. Le repli en dur
/// n'est pas une redondance : si une clé disparaissait des jetons, une résolution
/// qui échouerait laisserait le glyphe sans pinceau, donc invisible — une panne de
/// thème doit dégrader la couleur, pas effacer le contenu.
/// </para>
/// </summary>
internal static class StatePalette
{
    /// <summary>Clé de jeton, et couleur de repli, pour chaque état.</summary>
    private static (string Key, Color Fallback) For(IslandActivityState state) => state switch
    {
        // Un appel demande une décision : c'est le seul état qui justifie une
        // teinte d'alerte, parce que c'est le seul qui attende une réponse.
        IslandActivityState.CallActive => ("NfStateAlertBrush", Color.FromArgb(0xFF, 0xF0, 0x83, 0x6B)),

        // Ce qui progresse — un transfert, un compte à rebours — porte l'ambre
        // de la référence retenue.
        IslandActivityState.DownloadActive => ("NfStateWorkingBrush", Color.FromArgb(0xFF, 0xE8, 0xA6, 0x6B)),
        IslandActivityState.TimerActive => ("NfStateWorkingBrush", Color.FromArgb(0xFF, 0xE8, 0xA6, 0x6B)),

        // Un périphérique qui vient de se connecter est une bonne nouvelle
        // discrète.
        IslandActivityState.DeviceActive => ("NfStateSuccessBrush", Color.FromArgb(0xFF, 0x5F, 0xD0, 0x8A)),

        // Le reste informe : média, notification, dépôt de fichier.
        IslandActivityState.MediaActive => ("NfStateInfoBrush", Color.FromArgb(0xFF, 0x7F, 0xB3, 0xF0)),
        IslandActivityState.Notification => ("NfStateInfoBrush", Color.FromArgb(0xFF, 0x7F, 0xB3, 0xF0)),
        IslandActivityState.FileDrag => ("NfStateInfoBrush", Color.FromArgb(0xFF, 0x7F, 0xB3, 0xF0)),

        // Un retour système — volume, luminosité — n'a rien à signaler : il
        // affiche une valeur. Une teinte y ajouterait un sens qui n'existe pas.
        _ => ("NfStateNeutralBrush", Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF))
    };

    /// <summary>
    /// Nom de l'état, tel qu'il apparaît dans la légende de la carte.
    ///
    /// Deux états renvoient une chaîne vide, et c'est délibéré : la veille n'a
    /// rien à annoncer, et un retour système — le volume, la luminosité — affiche
    /// une valeur, pas une situation. Nommer l'un ou l'autre ajouterait un mot
    /// sans information, ce qui est précisément ce que la carte doit éviter.
    /// </summary>
    public static string Label(IslandActivityState state) => state switch
    {
        IslandActivityState.MediaActive => "En lecture",
        IslandActivityState.CallActive => "Appel en cours",
        IslandActivityState.DownloadActive => "En cours",
        IslandActivityState.FileDrag => "Dépôt",
        IslandActivityState.Notification => "Notification",
        IslandActivityState.TimerActive => "Minuteur",
        IslandActivityState.DeviceActive => "Périphérique",
        _ => string.Empty
    };

    /// <summary>Pinceau de la teinte d'un état.</summary>
    public static Brush Brush(IslandActivityState state)
    {
        (string key, Color fallback) = For(state);

        if (Application.Current?.Resources?.TryGetValue(key, out object? value) == true
            && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }

    /// <summary>
    /// Teinte d'un état, pour les surfaces qui ne passent pas par XAML — le halo
    /// du compositeur, par exemple.
    /// </summary>
    public static Color Tint(IslandActivityState state)
    {
        (string key, Color fallback) = For(state);

        if (Application.Current?.Resources?.TryGetValue(key, out object? value) == true)
        {
            if (value is SolidColorBrush solid)
            {
                return solid.Color;
            }

            if (value is Color color)
            {
                return color;
            }
        }

        return fallback;
    }
}
