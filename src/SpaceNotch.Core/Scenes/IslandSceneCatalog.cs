using System.Collections.Generic;

namespace SpaceNotch.Core.Scenes;

/// <summary>
/// Répertoire des scènes connues de l'Island.
///
/// C'est la pièce qui matérialise la règle « l'interface ne décide jamais » :
/// une fonctionnalité déclare une clé de scène, la fenêtre résout cette clé en
/// vue et en dimensions. Le rendu ne contient donc aucune chaîne de conditions
/// sur le type de contenu reçu.
/// </summary>
public static class IslandSceneCatalog
{
    public const string Pill = "pill";
    public const string Media = "media";
    public const string VolumeHud = "volume-hud";
    public const string BrightnessHud = "brightness-hud";
    public const string Notification = "notification";
    public const string Bluetooth = "bluetooth";
    public const string FileShelf = "file-shelf";
    public const string DropZone = "drop-zone";
    public const string Pomodoro = "pomodoro";
    public const string Timer = "timer";
    public const string Clipboard = "clipboard";
    public const string Launcher = "launcher";

    /// <summary>Couleur copiée (F5) : la nuance et ses trois formats.</summary>
    public const string Color = "color";

    /// <summary>Note éclair (F7) : un petit bloc-notes dans la notch.</summary>
    public const string Note = "note";

    /// <summary>Retour du calme (F9) : ce qui est arrivé, groupé par application.</summary>
    public const string Quiet = "quiet";

    /// <summary>Moniteur système (F6) : le processus gourmand, la courbe, la fermeture.</summary>
    public const string Monitor = "monitor";

    /// <summary>Menu rapide : clic droit sur la notch, qui s'ouvre en menu.</summary>
    public const string QuickMenu = "quick-menu";

    /// <summary>Présentation du premier lancement : cinq cartes, une par geste.</summary>
    public const string Welcome = "welcome";

    /// <summary>
    /// Scène générique destinée au contenu qui n'a pas de vue dédiée, et
    /// notamment à celui d'un greffon tiers.
    ///
    /// Elle existe parce qu'un auteur de greffon n'a aucun moyen de modifier la
    /// fenêtre : sans clé prévue pour lui, sa seule ressource serait de détourner
    /// une scène existante — déclarer son contenu météo comme « Bluetooth » pour
    /// qu'il s'affiche — ce qui serait un mensonge sémantique inscrit dans son
    /// code. Cette clé est le contrat qui lui évite cela. Voir ADR-010.
    /// </summary>
    public const string Card = "card";

    /// <summary>
    /// Largeur ajoutée à chaque scène pour les deux épaules de la silhouette
    /// TopAttached. Les largeurs ci-dessous restent celles du contenu : les
    /// épaules sont hors contenu, et les compter à part évite qu'un changement
    /// d'épaule ne rogne silencieusement une scène.
    /// </summary>
    private const double Shoulders = 2 * NotchGeometry.DefaultShoulder;

    // Chaque scène se déclare par la taille de son contenu ; SceneInsets y
    // ajoute la même marge partout, et les épaules.
    private static readonly Dictionary<string, IslandFootprint> Footprints = new()
    {
        // La clé « pill » ne désigne plus une forme propre : elle signifie « cette
        // activité n'a pas de scène déployée ». Ouverte, elle présente donc la
        // carte, seule forme qui accueille un titre et un sous-titre. La forme au
        // repos, elle, ne dépend pas de cette clé : voir IslandPresentation.
        [Pill] = SceneInsets.Wrap(212, 30),

        // Pochette 76, titre, artiste, ligne de lecture et contrôles.
        [Media] = SceneInsets.Wrap(356, 122),
        [VolumeHud] = SceneInsets.Wrap(268, 50),
        [BrightnessHud] = SceneInsets.Wrap(268, 50),
        [Notification] = SceneInsets.Wrap(330, 60),
        [Bluetooth] = SceneInsets.Wrap(284, 52),
        [FileShelf] = SceneInsets.Wrap(296, 94),
        [DropZone] = new IslandFootprint(260 + Shoulders, 75),

        // Chiffres, mode, puis la rangée de contrôles.
        [Pomodoro] = SceneInsets.Wrap(188, 96),
        [Timer] = SceneInsets.Wrap(188, 96),
        [Clipboard] = SceneInsets.Wrap(352, 208),

        // Nuance de 56, trois formats à recopier.
        [Color] = SceneInsets.Wrap(300, 76),

        // Une zone de texte de quatre lignes et son pied.
        [Note] = SceneInsets.Wrap(340, 120),

        // Le résumé du calme : jusqu'à quatre applications, une ligne chacune.
        [Quiet] = SceneInsets.Wrap(320, 112),
        [Monitor] = SceneInsets.Wrap(320, 96),

        // Le lanceur occupe la surface la plus large du répertoire : c'est la
        // seule scène qui présente une grille et un champ de recherche.
        [Launcher] = new IslandFootprint(SpaceNotch.Core.Launcher.LauncherLayout.Width, SpaceNotch.Core.Launcher.LauncherLayout.EmptyHeight),

        // La présentation : illustration, titre, deux lignes, pied. Sans épaules.
        [Welcome] = SceneInsets.Wrap(404, 204, shoulders: false),

        // Le menu rapide : sa hauteur exacte vient de QuickMenuLayout.
        [QuickMenu] = new IslandFootprint(SpaceNotch.Core.Menu.QuickMenuLayout.Width, SpaceNotch.Core.Menu.QuickMenuLayout.Height),

        // La carte générique est dimensionnée pour un titre, un sous-titre et une
        // rangée de deux ou trois contrôles. Elle est plus haute qu'un HUD : un
        // contenu tiers annonce souvent de quoi expliquer sa valeur, pas seulement
        // la valeur.
        [Card] = SceneInsets.Wrap(312, 110)
    };

    /// <summary>Clés déclarées, utile pour valider une activité en amont.</summary>
    public static IReadOnlyCollection<string> AllKeys => Footprints.Keys;

    public static bool IsKnown(string sceneKey) => Footprints.ContainsKey(sceneKey);

    /// <summary>
    /// Encombrement d'une scène. Une clé inconnue retombe sur la pilule fermée
    /// plutôt que de lever : une fonctionnalité tierce mal configurée ne doit pas
    /// pouvoir faire disparaître l'Island de l'écran.
    /// </summary>
    public static IslandFootprint FootprintFor(string? sceneKey)
    {
        if (sceneKey is not null && Footprints.TryGetValue(sceneKey, out var footprint))
        {
            return footprint;
        }

        return IslandFootprint.Collapsed;
    }
}
