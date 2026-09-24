namespace SpaceNotch_App.Diagnostics;

/// <summary>
/// Chemin par lequel la dissolution atmosphérique du bas de l'Island est
/// réellement calculée.
///
/// Le projet vise un fondu sans arête, et deux moyens d'y parvenir coexistent :
/// un masque calculé par le compositeur (référence), et un dégradé XAML (repli).
/// Tant que le chemin actif n'était pas exposé, les deux étaient
/// indistinguables à l'exécution — y compris pour un rapport de performance qui
/// aurait attribué au compositeur le coût du dégradé. Cette énumération rend la
/// distinction observable. Voir ADR-008.
/// </summary>
public enum AtmosphereRenderPath
{
    /// <summary>Aucune surface n'a encore été attachée : état initial.</summary>
    NotProbed = 0,

    /// <summary>Masque porté par le compositeur : le passage à l'alpha zéro est calculé par le GPU.</summary>
    Composition = 1,

    /// <summary>
    /// Repli sur le dégradé XAML. Soit l'utilisateur l'a demandé explicitement,
    /// soit le compositeur a refusé la surface — auquel cas l'Island s'affiche
    /// quand même, sans dissolution exacte.
    /// </summary>
    XamlFallback = 2
}
