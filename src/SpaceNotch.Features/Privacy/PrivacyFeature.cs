using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Privacy;

namespace SpaceNotch.Features.Privacy;

/// <summary>
/// Traduction pure des capteurs en cours d'utilisation en activités : un appel
/// quand une application de communication tient le micro, un enregistrement
/// sinon. Testable sans Windows.
/// </summary>
public static class PrivacyActivities
{
    public const string ActivityIdPrefix = "privacy.";

    /// <summary>
    /// Applications dont l'usage du micro est un appel. La liste est courte à
    /// dessein : se tromper dans ce sens — montrer « micro actif » pendant un
    /// appel — ne coûte presque rien, alors que qualifier d'appel un
    /// enregistrement tromperait.
    /// </summary>
    private static readonly string[] CallApps =
    [
        "discord", "teams", "ms-teams", "msteams", "zoom", "skype", "slack", "whatsapp",
        "webex", "ciscowebex", "signal", "telegram", "facetime", "mumble", "teamspeak", "ts3client",
        "element", "jitsi", "googlemeet", "gotomeeting", "ringcentral"
    ];

    /// <summary>Nom lisible d'une application à partir de son identifiant de consentement.</summary>
    public static string AppNameOf(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return "Application";
        }

        // Chemin d'exécutable : le magasin remplace les séparateurs par des #.
        if (appId.Contains('#', StringComparison.Ordinal) || appId.Contains('\\', StringComparison.Ordinal))
        {
            string file = appId.Split('#', '\\', '/').Last(part => part.Length > 0);
            int dot = file.LastIndexOf('.');
            string name = dot > 0 ? file[..dot] : file;

            return Prettify(name);
        }

        // Nom de famille de paquet : « Éditeur.Produit_hachage ».
        string family = appId.Split('_')[0];
        string product = family.Split('.').Last(part => part.Length > 0);

        return Prettify(product);
    }

    /// <summary>Majuscule initiale, le reste tel quel : « discord » devient « Discord », « MSTeams » ne change pas.</summary>
    private static string Prettify(string name)
    {
        string trimmed = name.Trim();

        return trimmed.Length == 0
            ? "Application"
            : char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    /// <summary>Vrai si l'application est une application d'appel connue.</summary>
    public static bool IsCallApp(string appId)
    {
        string key = new string(AppNameOf(appId).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        return CallApps.Any(app => key.StartsWith(app.Replace("-", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal));
    }

    /// <summary>
    /// Activités à publier pour les capteurs utilisés : une par application, qui
    /// dit ce qu'elle tient. Ordre stable, pour qu'une republication identique
    /// ne fasse rien bouger.
    /// </summary>
    public static IReadOnlyList<IslandActivity> Build(IEnumerable<CapabilityUsage> usages, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(usages);

        var activities = new List<IslandActivity>();

        foreach (IGrouping<string, CapabilityUsage> app in usages
                     .Where(u => !string.IsNullOrWhiteSpace(u.AppId))
                     .GroupBy(u => u.AppId, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            bool microphone = app.Any(u => u.Kind == CapabilityKind.Microphone);
            bool camera = app.Any(u => u.Kind == CapabilityKind.Camera);
            bool call = microphone && IsCallApp(app.Key);
            string name = AppNameOf(app.Key);

            string title = call
                ? (camera ? "Appel vidéo" : "Appel en cours")
                : (microphone && camera ? "Micro et caméra actifs" : camera ? "Caméra active" : "Micro actif");

            activities.Add(new IslandActivity
            {
                Id = ActivityIdPrefix + app.Key.ToLowerInvariant(),
                FeatureId = FeatureKeys.Privacy,
                SceneKey = IslandSceneCatalog.Card,
                Title = title,
                Eyebrow = name,
                Source = name,
                IconKey = call ? "Call" : camera ? "Camera" : "Microphone",
                State = call ? IslandActivityState.CallActive : IslandActivityState.DeviceActive,
                Priority = ActivityPriority.Normal,
                Policy = ActivityPresentationPolicy.Passive,
                Presentation = IslandPresentationTier.Signal,
                Role = call ? ActivityRole.Call : ActivityRole.Recording,
                CreatedAt = now
            });
        }

        return activities;
    }
}

/// <summary>
/// Appels et enregistrements en cours, d'après l'indicateur de confidentialité
/// de Windows : l'application qui tient le micro ou la caméra, et ce qu'elle en
/// fait. C'est ce qui donne une bulle à un appel pendant que la musique occupe
/// la notch. Voir ADR-019.
/// </summary>
public sealed class PrivacyFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Privacy;

    private readonly ICapabilityUsageSource _source;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, IslandActivity> _published = new(StringComparer.Ordinal);

    public PrivacyFeature(
        IActivityManager activities,
        IEventBus events,
        ICapabilityUsageSource source,
        bool isEnabled = true,
        Func<DateTimeOffset>? clock = null)
        : base(FeatureKey, "Micro et caméra", activities, events, isEnabled)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _source.Changed += OnChanged;
        _source.StartWatching();
        Refresh();

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _source.Changed -= OnChanged;
        _source.StopWatching();

        lock (_gate)
        {
            foreach (string id in _published.Keys)
            {
                RemoveActivity(id);
            }

            _published.Clear();
        }

        return Task.CompletedTask;
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>
    /// Relit les capteurs et publie la différence : une activité par application
    /// qui les tient, retirée dès qu'elle les rend. Une activité inchangée n'est
    /// pas republiée — elle garderait sa date, et sa place dans la pile.
    /// </summary>
    public void Refresh()
    {
        IReadOnlyList<CapabilityUsage> usages;

        try
        {
            usages = _source.Snapshot();
        }
        catch (Exception ex)
        {
            ReportError(ex);
            return;
        }

        lock (_gate)
        {
            IReadOnlyList<IslandActivity> current = PrivacyActivities.Build(usages, _clock());
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (IslandActivity activity in current)
            {
                seen.Add(activity.Id);

                if (_published.TryGetValue(activity.Id, out IslandActivity? previous)
                    && previous.Title == activity.Title
                    && previous.IconKey == activity.IconKey)
                {
                    continue;
                }

                _published[activity.Id] = activity;
                PublishActivity(activity);
            }

            foreach (string gone in _published.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                _published.Remove(gone);
                RemoveActivity(gone);
            }
        }
    }

    protected override void OnDisposed() => _source.Dispose();
}
