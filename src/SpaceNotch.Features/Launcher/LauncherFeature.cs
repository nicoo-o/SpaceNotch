using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Launcher;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Launcher;

namespace SpaceNotch.Features.Launcher;

/// <summary>Où la recherche range ses favoris, récents et fréquences entre deux sessions.</summary>
public interface ILauncherHistoryStore
{
    LauncherHistory Load();

    void Save(LauncherHistory history);
}

/// <summary>
/// La recherche, façon Spotlight et Raycast : applications (bureau et Store),
/// paramètres de Windows, fichiers récents, calcul et recherche web.
///
/// <para>
/// Le classement vit dans le cœur (<see cref="LauncherSearch"/>) ; ici, les
/// sources et les gestes. Le catalogue est un instantané immuable, remplacé en
/// bloc quand une lecture se termine : la frappe, sur le fil d'interface, ne
/// croise jamais une liste en cours de modification.
/// </para>
/// </summary>
public sealed class LauncherFeature : IslandFeatureBase
{
    public const string FeatureKey = "feature.launcher";

    public const string SearchAction = "launcher.search";
    public const string OpenAction = "launcher.open";
    public const string PinAction = "launcher.pin";
    public const string LocationAction = "launcher.location";
    public const string AdminAction = "launcher.admin";
    public const string UninstallAction = "launcher.uninstall";
    public const string DismissAction = "launcher.dismiss";

    /// <summary>Le panneau d'actions s'ouvre (« 1 ») ou se ferme (« 0 ») : la notch s'agrandit pour lui.</summary>
    public const string ActionsPanelAction = "launcher.actions";

    /// <summary>Ancien nom de l'action d'ouverture, gardé pour les greffons.</summary>
    public const string LaunchAction = OpenAction;

    private const string ActivityId = "feature.launcher.current";

    /// <summary>Le catalogue des applications est relu au plus toutes les deux minutes.</summary>
    private static readonly TimeSpan CatalogueLifetime = TimeSpan.FromMinutes(2);

    private readonly ILauncherHistoryStore? _store;
    private readonly bool _french = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
    private readonly LauncherText _text = LauncherText.For(CultureInfo.CurrentUICulture);

    private volatile IReadOnlyList<LauncherCandidate> _apps = [];
    private volatile IReadOnlyList<LauncherCandidate> _files = [];
    private readonly List<LauncherCandidate> _settings;

    private LauncherHistory _history = new();
    private IReadOnlyList<LauncherSection> _lastSections = [];
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private int _loading;
    private string _query = string.Empty;
    private bool _shown;
    private bool _actionsOpen;

    public LauncherFeature(IActivityManager activities, IEventBus events, bool isEnabled = true, ILauncherHistoryStore? store = null)
        : base(FeatureKey, "Recherche", activities, events, isEnabled)
    {
        _store = store;
        string subtitle = _french ? "Paramètre Windows" : "Windows setting";

        _settings = WindowsSettingsCatalog.All
            .Select(s => new LauncherCandidate(
                LauncherResultKind.Setting,
                WindowsSettingsCatalog.NameFor(s, _french),
                subtitle,
                s.Uri,
                Keywords: s.Keywords + " " + (_french ? s.English : s.French)))
            .ToList();
    }

    /// <summary>Raccourci global retenu, affiché dans le pied de la recherche.</summary>
    public string? Hotkey { get; set; }

    /// <summary>Nombre d'applications du catalogue, exposé aux diagnostics.</summary>
    public int CatalogueSize => _apps.Count;

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _history = LoadHistory();
        _ = RefreshAsync();
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _apps = [];
        _files = [];
        _query = string.Empty;
        _shown = false;
        RemoveActivity(ActivityId);
        return Task.CompletedTask;
    }

    /// <summary>Montre la recherche, champ vide. Relit les sources si elles ont vieilli.</summary>
    public void Show()
    {
        _query = string.Empty;
        _shown = true;
        _actionsOpen = false;
        Publish();

        if (DateTimeOffset.UtcNow - _loadedAt > CatalogueLifetime)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>
    /// Retire la recherche. Appelé quand la notch se referme : sans cela, elle
    /// restait l'activité présentée — devant la musique.
    /// </summary>
    public void Dismiss()
    {
        _query = string.Empty;
        _shown = false;
        _actionsOpen = false;
        RemoveActivity(ActivityId);
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.ActionId)
        {
            case SearchAction:
                _query = request.Value ?? string.Empty;
                Publish();
                return Task.FromResult(true);

            case OpenAction:
                return Task.FromResult(Open(request.Value, admin: false));

            case AdminAction:
                return Task.FromResult(Open(request.Value, admin: true));

            case PinAction when request.Value is { Length: > 0 } id:
                _history.TogglePin(id);
                SaveHistory();
                Publish();
                return Task.FromResult(true);

            case LocationAction when request.Value is { Length: > 0 } target:
                LauncherShell.OpenLocation(target);
                Dismiss();
                return Task.FromResult(true);

            case UninstallAction:
                LauncherShell.Uninstall();
                Dismiss();
                return Task.FromResult(true);

            case DismissAction:
                Dismiss();
                return Task.FromResult(true);

            case ActionsPanelAction:
                bool open = request.Value == "1";

                if (open != _actionsOpen)
                {
                    _actionsOpen = open;
                    Publish();
                }

                return Task.FromResult(true);

            default:
                return Task.FromResult(false);
        }
    }

    private bool Open(string? id, bool admin)
    {
        LauncherResult? result = _lastSections.SelectMany(s => s.Items)
            .FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

        string? target = result?.Target ?? id;

        if (string.IsNullOrWhiteSpace(target) || !LauncherShell.Open(target, admin))
        {
            return false;
        }

        // Le calcul et le web ne sont pas des « récents » : on ne retient que
        // ce qu'on rouvrira.
        if (result is null || result.Kind is LauncherResultKind.Application or LauncherResultKind.Setting or LauncherResultKind.File)
        {
            _history.RecordLaunch(result?.Id ?? target);
            SaveHistory();
        }

        // La recherche s'efface après avoir lancé : l'utilisateur a obtenu ce
        // qu'il voulait.
        Dismiss();
        return true;
    }

    private async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _loading, 1) == 1)
        {
            return;
        }

        try
        {
            if (_shown)
            {
                Publish();
            }

            IReadOnlyList<LauncherCandidate> apps = await AppCatalog.LoadAsync(
                _french ? "Application" : "Application",
                _french ? "Application du Store" : "Store app",
                ReportMessage).ConfigureAwait(false);

            IReadOnlyList<LauncherCandidate> files = await Task.Run(RecentFiles.Load).ConfigureAwait(false);

            _apps = apps;
            _files = files;
            _loadedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
        finally
        {
            Volatile.Write(ref _loading, 0);
        }

        if (_shown)
        {
            Publish();
        }
    }

    private void Publish()
    {
        var candidates = new List<LauncherCandidate>(_apps.Count + _settings.Count + _files.Count);
        candidates.AddRange(_apps);
        candidates.AddRange(_settings);
        candidates.AddRange(_files);

        IReadOnlyList<LauncherSection> sections = LauncherSearch.Build(_query, candidates, _history, _text, CultureInfo.CurrentCulture);
        _lastSections = sections;

        bool loading = Volatile.Read(ref _loading) == 1 && _apps.Count == 0;

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Launcher,
            Title = _french ? "Recherche" : "Search",
            Subtitle = _query,
            Source = "Launcher",
            IconKey = "Launcher",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,

            // La notch prend la hauteur de ce qu'elle montre.
            ExpandedFootprint = LauncherLayout.FootprintFor(sections, _actionsOpen),

            // Pendant la première lecture du catalogue — un vrai travail —, la
            // grille « Search » le dit. Filtrer une liste chargée est instantané.
            MotionState = loading ? ActivityMotionState.Working : ActivityMotionState.Idle,
            MotionPreset = HypnoticPreset.Search,
            Actions =
            [
                new ActivityAction(OpenAction, "Ouvrir", "Open", ActivityActionKind.Open, IsPrimary: true),
                new ActivityAction(SearchAction, "Rechercher", "Find")
            ],
            Payload = new LauncherPayload(sections, _query, loading, Hotkey, _history.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase))
        });
    }

    private LauncherHistory LoadHistory()
    {
        try
        {
            return _store?.Load() ?? new LauncherHistory();
        }
        catch (Exception ex)
        {
            ReportError(ex);
            return new LauncherHistory();
        }
    }

    private void SaveHistory()
    {
        try
        {
            _store?.Save(_history);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void ReportMessage(string message) => ReportError(new InvalidOperationException(message));
}
