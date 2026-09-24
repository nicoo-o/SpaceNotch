using System.Collections.Generic;

namespace NotchFlow.Core.Scenes;

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

    private static readonly Dictionary<string, IslandFootprint> Footprints = new()
    {
        // La clé « pill » ne désigne plus une forme propre : elle signifie « cette
        // activité n'a pas de scène déployée ». Ouverte, elle présente donc la
        // carte, seule forme qui accueille un titre et un sous-titre. La forme au
        // repos, elle, ne dépend pas de cette clé : voir IslandPresentation.
        [Pill] = new IslandFootprint(240 + Shoulders, 52),
        [Media] = new IslandFootprint(380 + Shoulders, 140),
        [VolumeHud] = new IslandFootprint(300 + Shoulders, 70),
        [BrightnessHud] = new IslandFootprint(300 + Shoulders, 70),
        [Notification] = new IslandFootprint(360 + Shoulders, 80),
        [Bluetooth] = new IslandFootprint(320 + Shoulders, 78),
        [FileShelf] = new IslandFootprint(320 + Shoulders, 120),
        [DropZone] = new IslandFootprint(260 + Shoulders, 75),
        [Pomodoro] = new IslandFootprint(220 + Shoulders, 78),
        [Timer] = new IslandFootprint(220 + Shoulders, 78),
        [Clipboard] = new IslandFootprint(380 + Shoulders, 230),

        // Le lanceur occupe la surface la plus large du répertoire : c'est la
        // seule scène qui présente une grille et un champ de recherche.
        [Launcher] = new IslandFootprint(440 + Shoulders, 260),

        // La carte générique est dimensionnée pour un titre, un sous-titre et une
        // rangée de deux ou trois contrôles. Elle est plus haute qu'un HUD : un
        // contenu tiers annonce souvent de quoi expliquer sa valeur, pas seulement
        // la valeur.
        [Card] = new IslandFootprint(340 + Shoulders, 132)
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
