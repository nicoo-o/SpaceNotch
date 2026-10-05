using System;
using System.Linq;
using System.Text.Json;

namespace SpaceNotch.Core.Update;

/// <summary>Ce que fait la notch quand une nouvelle version existe.</summary>
public enum UpdateMode
{
    /// <summary>Jamais de vérification.</summary>
    Off = 0,

    /// <summary>Vérifie, télécharge, et propose d'installer dans la notch.</summary>
    Notify = 1,

    /// <summary>Vérifie, télécharge, et installe seule au premier moment calme.</summary>
    Automatic = 2
}

/// <summary>Une version publiée, telle que GitHub la décrit.</summary>
/// <param name="Version">Numéro de version.</param>
/// <param name="Tag">Étiquette (« v1.17.0 »).</param>
/// <param name="PageUrl">Page de la release, pour qui veut lire les nouveautés.</param>
/// <param name="SetupUrl">Lien de téléchargement de l'installeur.</param>
/// <param name="SetupSize">Taille annoncée de l'installeur, en octets.</param>
/// <param name="ChecksumsUrl">Lien du fichier d'empreintes, ou <c>null</c> s'il manque.</param>
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string SetupUrl, long SetupSize, string? ChecksumsUrl);

/// <summary>Ce que la notch doit faire maintenant.</summary>
public enum UpdateAction
{
    /// <summary>Rien : à jour, désactivé, ou release inutilisable.</summary>
    None,

    /// <summary>Télécharger l'installeur (puis le vérifier).</summary>
    Download,

    /// <summary>Proposer l'installation dans la notch.</summary>
    Offer,

    /// <summary>Installer maintenant, en silence.</summary>
    Install,

    /// <summary>Attendre un moment plus calme.</summary>
    Wait
}

/// <summary>
/// Les règles de la mise à jour automatique : quand vérifier, quoi télécharger,
/// quand installer. Pures et testées ; le réseau, le disque et l'installeur
/// vivent dans l'application.
/// </summary>
public static class UpdateRules
{
    /// <summary>Le dépôt dont les releases sont suivies.</summary>
    public const string Repository = "nicoo-o/SpaceNotch";

    /// <summary>Adresse de la dernière release publiée.</summary>
    public const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    /// <summary>Nom de l'installeur dans une release.</summary>
    public const string SetupAsset = "SpaceNotch-Setup.exe";

    /// <summary>Nom du fichier d'empreintes dans une release.</summary>
    public const string ChecksumsAsset = "SHA256SUMS.txt";

    /// <summary>Première vérification après le démarrage : la session se pose d'abord.</summary>
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(2);

    /// <summary>Intervalle entre deux vérifications (60 requêtes par heure au plus pour GitHub sans compte).</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    /// <summary>Inactivité qui fait un « moment calme » pour installer.</summary>
    public static readonly TimeSpan QuietIdle = TimeSpan.FromMinutes(3);

    /// <summary>« Plus tard » repousse la proposition d'autant.</summary>
    public static readonly TimeSpan Postpone = TimeSpan.FromHours(20);

    /// <summary>Taille plausible d'un installeur : au-delà, la release est refusée.</summary>
    public const long MaximumSetupSize = 600L * 1024 * 1024;

    /// <summary>Lit un numéro de version dans une étiquette (« v1.17.0 », « 1.17 »).</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        string text = tag.Trim().TrimStart('v', 'V');
        int dash = text.IndexOfAny(['-', '+']);

        if (dash >= 0)
        {
            text = text[..dash];
        }

        return Version.TryParse(text, out Version? version) && version.Major >= 0
            ? Normalize(version)
            : null;
    }

    /// <summary>Trois composantes (majeure, mineure, correctif), la quatrième ignorée.</summary>
    public static Version Normalize(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new Version(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));
    }

    /// <summary>Vrai si <paramref name="candidate"/> est plus récente que <paramref name="current"/>.</summary>
    public static bool IsNewer(Version candidate, Version current)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(current);
        return Normalize(candidate) > Normalize(current);
    }

    /// <summary>
    /// Lit la réponse de GitHub (« releases/latest »). Rend <c>null</c> pour un
    /// brouillon, une préversion, une étiquette illisible, un installeur absent
    /// ou d'une taille invraisemblable, ou un lien qui ne mène pas à GitHub.
    /// </summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (Bool(root, "draft") || Bool(root, "prerelease"))
            {
                return null;
            }

            string? tag = Text(root, "tag_name");

            if (ParseTag(tag) is not { } version || !root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? setupUrl = null;
            long setupSize = 0;
            string? checksumsUrl = null;

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string? name = Text(asset, "name");
                string? url = Text(asset, "browser_download_url");

                if (!IsGitHubDownload(url))
                {
                    continue;
                }

                if (string.Equals(name, SetupAsset, StringComparison.OrdinalIgnoreCase))
                {
                    setupUrl = url;
                    setupSize = asset.TryGetProperty("size", out JsonElement size) && size.TryGetInt64(out long bytes) ? bytes : 0;
                }
                else if (string.Equals(name, ChecksumsAsset, StringComparison.OrdinalIgnoreCase))
                {
                    checksumsUrl = url;
                }
            }

            if (setupUrl is null || setupSize <= 0 || setupSize > MaximumSetupSize)
            {
                return null;
            }

            string page = Text(root, "html_url") is { } html && html.StartsWith("https://github.com/" + Repository + "/", StringComparison.OrdinalIgnoreCase)
                ? html
                : "https://github.com/" + Repository + "/releases/tag/" + tag;

            return new ReleaseInfo(version, tag!, page, setupUrl, setupSize, checksumsUrl);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// L'empreinte SHA-256 d'un fichier dans un SHA256SUMS.txt (« empreinte  nom »),
    /// en minuscules, ou <c>null</c>.
    /// </summary>
    public static string? FindChecksum(string? sums, string fileName)
    {
        if (string.IsNullOrEmpty(sums))
        {
            return null;
        }

        foreach (string raw in sums.Split('\n'))
        {
            string line = raw.Trim();
            int space = line.IndexOf(' ', StringComparison.Ordinal);

            if (space != 64)
            {
                continue;
            }

            string hash = line[..64];
            string name = line[space..].Trim().TrimStart('*');

            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase) && hash.All(Uri.IsHexDigit))
            {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Décide du prochain pas pour une release plus récente.
    /// </summary>
    /// <param name="mode">Le réglage de l'utilisateur.</param>
    /// <param name="downloaded">L'installeur est téléchargé et vérifié.</param>
    /// <param name="installed">SpaceNotch est installée (et non portable) : on peut installer par-dessus.</param>
    /// <param name="idle">Temps depuis la dernière saisie.</param>
    /// <param name="busy">La notch est ouverte, ou une activité importante (appel, réunion) est en cours.</param>
    /// <param name="postponedUntil">« Plus tard » choisi par l'utilisateur, jusqu'à quand.</param>
    /// <param name="now">Maintenant.</param>
    public static UpdateAction Decide(UpdateMode mode, bool downloaded, bool installed, TimeSpan idle, bool busy, DateTimeOffset? postponedUntil, DateTimeOffset now)
    {
        if (mode == UpdateMode.Off)
        {
            return UpdateAction.None;
        }

        // Portable : rien à remplacer ; la proposition mène à la page.
        if (!installed)
        {
            return postponedUntil > now ? UpdateAction.None : UpdateAction.Offer;
        }

        if (!downloaded)
        {
            return UpdateAction.Download;
        }

        if (mode == UpdateMode.Notify)
        {
            return postponedUntil > now ? UpdateAction.None : UpdateAction.Offer;
        }

        // Automatique : seulement au calme, et jamais sous la main de l'utilisateur.
        return !busy && idle >= QuietIdle ? UpdateAction.Install : UpdateAction.Wait;
    }

    /// <summary>Vrai si l'application vient d'être mise à jour depuis le dernier lancement.</summary>
    public static bool JustUpdated(string? lastRunVersion, Version current)
        => ParseTag(lastRunVersion) is { } last && IsNewer(current, last);

    /// <summary>
    /// Vrai si la copie qui tourne peut surveiller et installer les mises à jour :
    /// la copie installée, ou une version portable quand rien n'est installé.
    /// Une autre copie — un build de développement lancé depuis son dossier
    /// <c>bin</c> — trouvait l'installation dans le registre, l'écrasait avec la
    /// dernière release et relançait la notch de l'utilisateur (2026-10-04).
    /// </summary>
    /// <param name="runningExecutable">Exécutable du processus, ou <c>null</c> s'il est inconnu.</param>
    /// <param name="installedExecutable">Exécutable installé, ou <c>null</c> sans installation.</param>
    public static bool MayUpdateItself(string? runningExecutable, string? installedExecutable)
    {
        if (installedExecutable is null)
        {
            return true;
        }

        return runningExecutable is not null
            && string.Equals(runningExecutable.Replace('/', '\\'), installedExecutable.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGitHubDownload(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/" + Repository + "/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    /// <summary>Texte d'une version pour l'affichage (« 1.17.0 »).</summary>
    public static string Display(Version version) => Normalize(version).ToString(3);
}
