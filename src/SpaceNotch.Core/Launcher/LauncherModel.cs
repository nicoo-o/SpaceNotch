namespace SpaceNotch.Core.Launcher;

/// <summary>Ce qu'est un résultat de la recherche.</summary>
public enum LauncherResultKind
{
    /// <summary>Une application : bureau, Store ou raccourci.</summary>
    Application = 0,

    /// <summary>Une page de Paramètres de Windows (<c>ms-settings:</c>).</summary>
    Setting,

    /// <summary>Un fichier ouvert récemment.</summary>
    File,

    /// <summary>Le résultat d'un calcul tapé dans le champ.</summary>
    Calculation,

    /// <summary>La recherche sur le web, en repli.</summary>
    Web
}

/// <summary>Un passage trouvé dans un nom : ce que la vue met en valeur.</summary>
public readonly record struct TextMatch(int Start, int Length);

/// <summary>Une ligne de la recherche.</summary>
/// <param name="Id">Identifiant stable : la cible, pour les favoris et récents.</param>
/// <param name="Kind">Nature du résultat.</param>
/// <param name="Title">Nom affiché.</param>
/// <param name="Subtitle">Ce qu'est la ligne : « Application », « Paramètre Windows », un dossier.</param>
/// <param name="Target">Ce qu'on ouvre : AUMID, chemin, URI ms-settings, valeur, adresse.</param>
/// <param name="Matches">Passages du nom qui correspondent à la requête.</param>
/// <param name="IconPath">Chemin de l'élément dont la plateforme extrait l'icône, s'il en a une.</param>
/// <param name="Glyph">Glyphe de repli quand il n'y a pas d'icône.</param>
public sealed record LauncherResult(
    string Id,
    LauncherResultKind Kind,
    string Title,
    string Subtitle,
    string Target,
    IReadOnlyList<TextMatch> Matches,
    string? IconPath = null,
    string? Glyph = null)
{
    /// <summary>Vrai si le panneau d'actions (Ctrl+K) a quelque chose à proposer pour cette ligne.</summary>
    public bool HasActions => Kind is LauncherResultKind.Application or LauncherResultKind.File;
}

/// <summary>Un groupe de résultats sous un titre : « Favoris », « Paramètres »…</summary>
public sealed record LauncherSection(string Title, IReadOnlyList<LauncherResult> Items);

/// <summary>Les actions du panneau Ctrl+K, dans l'ordre où il les présente.</summary>
public enum LauncherAction
{
    Open = 0,
    TogglePin,
    OpenLocation,
    RunAsAdministrator,
    Uninstall
}

/// <summary>Actions proposées pour un résultat, dans l'ordre du panneau.</summary>
public static class LauncherActions
{
    public static IReadOnlyList<LauncherAction> For(LauncherResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Kind switch
        {
            LauncherResultKind.Application =>
                [LauncherAction.Open, LauncherAction.TogglePin, LauncherAction.OpenLocation, LauncherAction.RunAsAdministrator, LauncherAction.Uninstall],
            LauncherResultKind.File =>
                [LauncherAction.Open, LauncherAction.TogglePin, LauncherAction.OpenLocation],
            _ => [LauncherAction.Open]
        };
    }
}
