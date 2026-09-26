namespace SpaceNotch.Core.Launcher;

/// <summary>
/// Ce que la recherche retient d'une session à l'autre : les favoris épinglés,
/// les derniers ouverts, et combien de fois chaque cible a été lancée. Les
/// identifiants sont les cibles des résultats.
/// </summary>
public sealed class LauncherHistory
{
    /// <summary>Nombre de récents conservés.</summary>
    public const int MaxRecents = 8;

    /// <summary>Nombre de cibles dont on retient la fréquence.</summary>
    public const int MaxCounted = 200;

    private readonly List<string> _favorites;
    private readonly List<string> _recents;
    private readonly Dictionary<string, int> _launches;

    public LauncherHistory(
        IEnumerable<string>? favorites = null,
        IEnumerable<string>? recents = null,
        IReadOnlyDictionary<string, int>? launches = null)
    {
        _favorites = favorites?.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _recents = recents?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRecents).ToList() ?? [];
        _launches = launches is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(launches, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> Favorites => _favorites;

    public IReadOnlyList<string> Recents => _recents;

    public IReadOnlyDictionary<string, int> Launches => _launches;

    public bool IsFavorite(string id) => _favorites.Contains(id, StringComparer.OrdinalIgnoreCase);

    public int LaunchesOf(string id) => _launches.TryGetValue(id, out int count) ? count : 0;

    /// <summary>Épingle ou désépingle. Renvoie le nouvel état.</summary>
    public bool TogglePin(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        int index = _favorites.FindIndex(f => string.Equals(f, id, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            _favorites.RemoveAt(index);
            return false;
        }

        _favorites.Add(id);
        return true;
    }

    /// <summary>Retient un lancement : en tête des récents, une fois de plus dans les fréquences.</summary>
    public void RecordLaunch(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        _recents.RemoveAll(r => string.Equals(r, id, StringComparison.OrdinalIgnoreCase));
        _recents.Insert(0, id);

        if (_recents.Count > MaxRecents)
        {
            _recents.RemoveRange(MaxRecents, _recents.Count - MaxRecents);
        }

        _launches[id] = LaunchesOf(id) + 1;

        // Les fréquences ne grossissent pas sans fin : on oublie les moins lancées.
        if (_launches.Count > MaxCounted)
        {
            foreach (string rare in _launches.OrderBy(p => p.Value).Take(_launches.Count - MaxCounted).Select(p => p.Key).ToList())
            {
                _launches.Remove(rare);
            }
        }
    }

    /// <summary>Oublie une cible disparue (application désinstallée, fichier supprimé).</summary>
    public void Forget(string id)
    {
        _favorites.RemoveAll(f => string.Equals(f, id, StringComparison.OrdinalIgnoreCase));
        _recents.RemoveAll(r => string.Equals(r, id, StringComparison.OrdinalIgnoreCase));
        _launches.Remove(id);
    }
}
