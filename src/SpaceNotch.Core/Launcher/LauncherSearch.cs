using System.Globalization;

namespace SpaceNotch.Core.Launcher;

/// <summary>Un élément cherchable, avant classement.</summary>
public sealed record LauncherCandidate(
    LauncherResultKind Kind,
    string Title,
    string Subtitle,
    string Target,
    string? IconPath = null,
    string? Keywords = null)
{
    /// <summary>Identifiant stable : la cible.</summary>
    public string Id => Target;
}

/// <summary>Les textes des sections et des lignes fixes, dans la langue de Windows.</summary>
public sealed record LauncherText(
    string Favorites,
    string Recents,
    string Applications,
    string Settings,
    string Files,
    string Calculation,
    string Web,
    string SearchWebFormat,
    string DefaultBrowser,
    string CopyResult)
{
    public static LauncherText French { get; } = new(
        "Favoris", "Récents", "Applications", "Paramètres", "Fichiers récents", "Calcul", "Web",
        "Rechercher « {0} » sur le web", "Navigateur par défaut", "Copier le résultat");

    public static LauncherText English { get; } = new(
        "Favorites", "Recent", "Applications", "Settings", "Recent files", "Calculation", "Web",
        "Search the web for “{0}”", "Default browser", "Copy result");

    public static LauncherText For(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TwoLetterISOLanguageName == "fr" ? French : English;
    }
}

/// <summary>
/// Assemble ce que montre la recherche. Champ vide : favoris puis récents,
/// jamais la liste alphabétique brute. Avec une requête : un calcul s'il y en
/// a un, puis les groupes classés par leur meilleur résultat, et la recherche
/// web en repli, toujours en dernier.
/// </summary>
public static class LauncherSearch
{
    public const int MaxApplications = 6;
    public const int MaxSettings = 3;
    public const int MaxFiles = 3;
    public const int MaxRestFavorites = 6;
    public const int MaxRestRecents = 5;

    public static IReadOnlyList<LauncherSection> Build(
        string? query,
        IReadOnlyList<LauncherCandidate> candidates,
        LauncherHistory history,
        LauncherText text,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(culture);

        string q = query?.Trim() ?? string.Empty;

        return q.Length == 0
            ? BuildRest(candidates, history, text)
            : BuildResults(q, candidates, history, text, culture);
    }

    private static List<LauncherSection> BuildRest(
        IReadOnlyList<LauncherCandidate> candidates,
        LauncherHistory history,
        LauncherText text)
    {
        var byId = new Dictionary<string, LauncherCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (LauncherCandidate candidate in candidates)
        {
            byId.TryAdd(candidate.Id, candidate);
        }

        var sections = new List<LauncherSection>();

        List<LauncherResult> favorites = history.Favorites
            .Select(id => byId.GetValueOrDefault(id))
            .OfType<LauncherCandidate>()
            .Take(MaxRestFavorites)
            .Select(c => ToResult(c, []))
            .ToList();

        if (favorites.Count > 0)
        {
            sections.Add(new LauncherSection(text.Favorites, favorites));
        }

        List<LauncherResult> recents = history.Recents
            .Where(id => !history.IsFavorite(id))
            .Select(id => byId.GetValueOrDefault(id))
            .OfType<LauncherCandidate>()
            .Take(MaxRestRecents)
            .Select(c => ToResult(c, []))
            .ToList();

        if (recents.Count > 0)
        {
            sections.Add(new LauncherSection(text.Recents, recents));
        }

        // Premier lancement, rien d'épinglé ni de lancé : les fichiers ouverts
        // récemment dans Windows plutôt qu'une liste alphabétique.
        if (sections.Count == 0)
        {
            List<LauncherResult> files = candidates
                .Where(c => c.Kind == LauncherResultKind.File)
                .Take(MaxRestRecents)
                .Select(c => ToResult(c, []))
                .ToList();

            if (files.Count > 0)
            {
                sections.Add(new LauncherSection(text.Files, files));
            }
        }

        return sections;
    }

    private static List<LauncherSection> BuildResults(
        string q,
        IReadOnlyList<LauncherCandidate> candidates,
        LauncherHistory history,
        LauncherText text,
        CultureInfo culture)
    {
        var groups = new List<(int Best, LauncherSection Section)>();

        AddGroup(LauncherResultKind.Application, text.Applications, MaxApplications);
        AddGroup(LauncherResultKind.Setting, text.Settings, MaxSettings);
        AddGroup(LauncherResultKind.File, text.Files, MaxFiles);

        var sections = new List<LauncherSection>();

        if (InlineCalculator.TryEvaluate(q, out double value))
        {
            string result = InlineCalculator.Format(value, culture);

            sections.Add(new LauncherSection(text.Calculation,
            [
                new LauncherResult("calc:" + q, LauncherResultKind.Calculation, "= " + result, q, result, [], Glyph: "=")
            ]));
        }

        sections.AddRange(groups.OrderByDescending(g => g.Best).Select(g => g.Section));

        string web = string.Format(CultureInfo.CurrentCulture, text.SearchWebFormat, q);
        sections.Add(new LauncherSection(text.Web,
        [
            new LauncherResult("web:" + q, LauncherResultKind.Web, web, text.DefaultBrowser,
                "https://www.bing.com/search?q=" + Uri.EscapeDataString(q), [], Glyph: "◎")
        ]));

        return sections;

        void AddGroup(LauncherResultKind kind, string title, int max)
        {
            var scored = new List<(int Score, LauncherResult Result)>();

            foreach (LauncherCandidate candidate in candidates)
            {
                if (candidate.Kind != kind)
                {
                    continue;
                }

                int score = LauncherRanking.Score(candidate.Title, q, out IReadOnlyList<TextMatch> matches);

                // Les mots-clés trouvent une page sans la mettre en valeur :
                // « wifi » trouve « Réseau et Internet ».
                if (score == 0 && candidate.Keywords is { } keywords
                    && LauncherRanking.Score(keywords, q, out _) is > 0 and var keywordScore)
                {
                    score = Math.Min(keywordScore, LauncherRanking.Substring) - 5;
                    matches = [];
                }

                if (score <= 0)
                {
                    continue;
                }

                score += LauncherRanking.UsageBonus(history.IsFavorite(candidate.Id), history.LaunchesOf(candidate.Id));
                scored.Add((score, ToResult(candidate, matches)));
            }

            if (scored.Count == 0)
            {
                return;
            }

            List<(int Score, LauncherResult Result)> top = scored
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Result.Title, StringComparer.Create(culture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace))
                .Take(max)
                .ToList();

            groups.Add((top[0].Score, new LauncherSection(title, top.Select(s => s.Result).ToList())));
        }
    }

    private static LauncherResult ToResult(LauncherCandidate candidate, IReadOnlyList<TextMatch> matches)
        => new(candidate.Id, candidate.Kind, candidate.Title, candidate.Subtitle, candidate.Target, matches, candidate.IconPath);
}
