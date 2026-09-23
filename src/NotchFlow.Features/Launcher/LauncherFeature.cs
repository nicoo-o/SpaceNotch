using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NotchFlow.Core.Activities;
using NotchFlow.Core.Events;
using NotchFlow.Core.Features;
using NotchFlow.Core.Scenes;
using NotchFlow.Core.State;

namespace NotchFlow.Features.Launcher;

/// <summary>
/// Lanceur d'applications : favorites, récemment utilisées et recherche.
///
/// Le catalogue est constitué en lisant les raccourcis du menu Démarrer, ce qui
/// évite d'inventorier le registre ou les paquets installés. Deux sources
/// alimentent le classement : les favorites, mémorisées dans les préférences, et
/// les applications lancées depuis l'Island pendant la session. Le classement est
/// fait ici, jamais par la vue.
/// </summary>
public sealed class LauncherFeature : IslandFeatureBase
{
    public const string FeatureKey = "feature.launcher";

    public const string LaunchAction = "launcher.launch";

    public const string SearchAction = "launcher.search";

    private const string ActivityId = "feature.launcher.current";

    /// <summary>Dossiers du menu Démarrer, par utilisateur et global.</summary>
    private static readonly string[] SearchRoots =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs")
    ];

    /// <summary>Nombre maximal d'applications présentées simultanément.</summary>
    private const int MaxVisible = 12;

    private readonly List<App> _catalogue = [];
    private readonly List<string> _recentlyLaunched = [];
    private readonly List<FileSystemWatcher> _watchers = [];

    private Timer? _rescanTimer;
    private string _query = string.Empty;

    public LauncherFeature(IActivityManager activities, IEventBus events, bool isEnabled = true)
        : base(FeatureKey, "Lanceur d'applications", activities, events, isEnabled)
    {
    }

    /// <summary>Nombre d'applications du catalogue, exposé aux diagnostics.</summary>
    public int CatalogueSize => _catalogue.Count;

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        Rescan();
        StartWatchers();

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();

        _rescanTimer?.Dispose();
        _rescanTimer = null;

        _catalogue.Clear();
        _recentlyLaunched.Clear();
        _query = string.Empty;

        RemoveActivity(ActivityId);

        return Task.CompletedTask;
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.ActionId)
        {
            case LaunchAction:
                return Task.FromResult(Launch(request.Value));

            case SearchAction:
                _query = request.Value ?? string.Empty;
                Publish();
                return Task.FromResult(true);

            default:
                return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Présente le lanceur. Appelé par l'hôte — depuis le menu de la zone de
    /// notification, par exemple.
    /// </summary>
    public void Show()
    {
        _query = string.Empty;
        Publish();
    }

    private bool Launch(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

            // Remonter l'application en tête de liste : c'est la seule notion de
            // « récent » que le système ne fournit pas — le registre des
            // applications récentes de Windows ne porte pas d'horodatage.
            _recentlyLaunched.RemoveAll(t => string.Equals(t, target, StringComparison.OrdinalIgnoreCase));
            _recentlyLaunched.Insert(0, target);

            while (_recentlyLaunched.Count > MaxVisible)
            {
                _recentlyLaunched.RemoveAt(_recentlyLaunched.Count - 1);
            }

            // Le lanceur s'efface après avoir lancé : rien ne justifie de rester
            // affiché après que l'utilisateur a obtenu ce qu'il voulait.
            RemoveActivity(ActivityId);

            return true;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            return false;
        }
    }

    /// <summary>
    /// Observe les dossiers du menu Démarrer.
    ///
    /// L'observation est événementielle : aucune analyse périodique. Les
    /// notifications du système de fichiers peuvent arriver en rafale pendant une
    /// installation, et sont donc regroupées par un minuteur à usage unique.
    /// </summary>
    private void StartWatchers()
    {
        foreach (string root in SearchRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                };

                watcher.Created += OnCatalogueChanged;
                watcher.Deleted += OnCatalogueChanged;
                watcher.Renamed += OnCatalogueChanged;
                watcher.EnableRaisingEvents = true;

                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                // Un dossier non observable n'empêche pas le lanceur de
                // fonctionner : le catalogue restera simplement figé. L'incident
                // est consigné, pas tu.
                ReportError(ex);
            }
        }
    }

    private void OnCatalogueChanged(object sender, FileSystemEventArgs e)
    {
        _rescanTimer ??= new Timer(_ => Rescan(), null, Timeout.Infinite, Timeout.Infinite);

        // Regroupement : une seule relecture pour une rafale de notifications.
        _rescanTimer.Change(TimeSpan.FromMilliseconds(600), Timeout.InfiniteTimeSpan);
    }

    private void Rescan()
    {
        var found = new List<App>();

        foreach (string root in SearchRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(file);

                    // Les désinstalleurs, aides et sites web encombrent le menu
                    // Démarrer sans jamais être lancés volontairement.
                    if (IsNoise(name))
                    {
                        continue;
                    }

                    found.Add(new App(name, file));
                }
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
        }

        _catalogue.Clear();
        _catalogue.AddRange(found
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()));
    }

    private static bool IsNoise(string name)
        => name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
            || name.Contains("désinstall", StringComparison.OrdinalIgnoreCase)
            || name.Contains("readme", StringComparison.OrdinalIgnoreCase)
            || name.Contains("help", StringComparison.OrdinalIgnoreCase)
            || name.Contains("aide", StringComparison.OrdinalIgnoreCase);

    private void Publish()
    {
        List<LauncherEntry> entries = Rank()
            .Take(MaxVisible)
            .Select(a => new LauncherEntry(
                Id: a.Path,
                Name: a.Name,
                Target: a.Path,
                IsRecent: _recentlyLaunched.Any(
                    t => string.Equals(t, a.Path, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Launcher,
            Title = "Applications",
            Subtitle = $"{_catalogue.Count} application{(_catalogue.Count > 1 ? "s" : string.Empty)}",
            Source = "Launcher",
            IconKey = "Launcher",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Actions =
            [
                new ActivityAction(LaunchAction, "Lancer", "Launch", ActivityActionKind.Open, IsPrimary: true),
                new ActivityAction(SearchAction, "Rechercher", "Find")
                // Les favorites modifiables par l'utilisateur ne sont pas encore
                // exposées : les afficher sans moyen de les définir serait une
                // promesse sans contenu.
            ],
            Payload = new LauncherPayload(entries, _query)
        });
    }

    /// <summary>
    /// Classement : correspondances de recherche d'abord, applications lancées
    /// pendant la session ensuite, puis reste du catalogue par ordre alphabétique.
    /// </summary>
    private List<App> Rank()
    {
        IEnumerable<App> matches = _catalogue;

        if (!string.IsNullOrWhiteSpace(_query))
        {
            string query = _query.Trim();

            matches = _catalogue.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        List<App> ordered = matches.ToList();

        ordered.Sort((left, right) =>
        {
            int leftRecent = _recentlyLaunched.FindIndex(t => string.Equals(t, left.Path, StringComparison.OrdinalIgnoreCase));
            int rightRecent = _recentlyLaunched.FindIndex(t => string.Equals(t, right.Path, StringComparison.OrdinalIgnoreCase));

            bool leftKnown = leftRecent >= 0;
            bool rightKnown = rightRecent >= 0;

            if (leftKnown != rightKnown)
            {
                return leftKnown ? -1 : 1;
            }

            if (leftKnown && rightKnown)
            {
                return leftRecent.CompareTo(rightRecent);
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        return ordered;
    }

    private sealed record App(string Name, string Path);
}
