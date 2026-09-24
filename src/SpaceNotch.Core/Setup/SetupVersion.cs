namespace SpaceNotch.Core.Setup;

/// <summary>Ce que fait l'installeur face à ce qui est déjà là.</summary>
public enum InstallKind
{
    /// <summary>Rien d'installé : installation, avec ses choix.</summary>
    Fresh = 0,

    /// <summary>Une version plus ancienne : mise à jour directe, choix repris.</summary>
    Update,

    /// <summary>La même version : réinstallation, choix repris.</summary>
    Reinstall,

    /// <summary>Une version plus récente : remplacement, sur confirmation.</summary>
    Downgrade
}

/// <summary>Comparaison de versions, tolérante aux formes qu'on rencontre : « v1.2 », « 1.2.0-beta », « 1.2.0+abc123 ».</summary>
public static class SetupVersion
{
    /// <summary>Classe l'installation à faire.</summary>
    /// <param name="installed">Version installée, ou <c>null</c> si rien n'est installé.</param>
    /// <param name="current">Version de cet installeur.</param>
    public static InstallKind Classify(string? installed, string current)
    {
        if (installed is null)
        {
            return InstallKind.Fresh;
        }

        Version? before = Parse(installed);
        Version? now = Parse(current);

        if (before is null || now is null)
        {
            // Une version illisible ne doit pas bloquer : on remet d'aplomb.
            return InstallKind.Update;
        }

        int order = now.CompareTo(before);

        return order > 0 ? InstallKind.Update : order == 0 ? InstallKind.Reinstall : InstallKind.Downgrade;
    }

    /// <summary>Lit une version sur trois ou quatre nombres, sans préfixe ni suffixe.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string core = text.Trim().TrimStart('v', 'V');
        int cut = core.IndexOfAny(['-', '+', ' ']);

        if (cut >= 0)
        {
            core = core[..cut];
        }

        if (!Version.TryParse(core.Contains('.', StringComparison.Ordinal) ? core : core + ".0", out Version? version))
        {
            return null;
        }

        // 1.2 et 1.2.0 sont la même version : les composantes absentes valent zéro.
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }

    /// <summary>Version affichée : trois nombres, le quatrième seulement s'il compte.</summary>
    public static string Display(string? text)
    {
        Version? version = Parse(text);

        if (version is null)
        {
            return text?.Trim() ?? string.Empty;
        }

        return version.Revision > 0 ? version.ToString(4) : version.ToString(3);
    }
}
