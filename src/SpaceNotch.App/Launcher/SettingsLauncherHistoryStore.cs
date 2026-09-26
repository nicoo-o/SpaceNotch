using System.Linq;
using SpaceNotch.Core.Launcher;
using SpaceNotch.Features.Launcher;
using SpaceNotch.Infrastructure.Config;

namespace SpaceNotch_App.Launcher;

/// <summary>
/// Favoris, récents et fréquences de la recherche, rangés dans la configuration.
/// L'écriture ne notifie pas les abonnés des réglages : épingler une application
/// ne doit pas faire réappliquer toute l'apparence de la notch.
/// </summary>
internal sealed class SettingsLauncherHistoryStore(SettingsService settings) : ILauncherHistoryStore
{
    public LauncherHistory Load()
    {
        AppSettings current = settings.Current;
        return new LauncherHistory(current.LauncherFavorites, current.LauncherRecents, current.LauncherLaunches);
    }

    public void Save(LauncherHistory history)
    {
        AppSettings current = settings.Current;
        current.LauncherFavorites = history.Favorites.ToList();
        current.LauncherRecents = history.Recents.ToList();
        current.LauncherLaunches = history.Launches.ToDictionary(p => p.Key, p => p.Value);
        settings.Persist();
    }
}
