using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Assistant;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Notifications;

namespace SpaceNotch.Features.Notifications;

/// <summary>
/// Notifications applicatives Windows (UserNotificationListener).
/// </summary>
public sealed class NotificationFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Notifications;

    /// <summary>
    /// Source système des notifications. Leur durée de vie dans la notch
    /// appartient aux groupes (<see cref="NotificationGroups.Lifetime"/>) : sans
    /// durée, chaque notification resterait indéfiniment dans le gestionnaire.
    /// </summary>
    private readonly WindowsNotificationListener _listener;
    private readonly NotificationGroups _groups = new();
    private readonly object _gate = new();

    /// <summary>
    /// Ne pas déranger (F9) : l'activité discrète — la lune et le compte —,
    /// seulement quand au moins une notification a été retenue. Un calme où
    /// rien n'arrive ne montre rien : la notch reste au repos.
    /// </summary>
    public const string QuietActivityId = "feature.notifications.quiet";

    /// <summary>Le résumé montré à la sortie du calme.</summary>
    public const string QuietSummaryActivityId = "feature.notifications.quiet-summary";

    /// <summary>
    /// Intervalle de lecture de l'état « Ne pas déranger ». Windows ne notifie
    /// pas ce changement : c'est une exception documentée à la règle « aucune
    /// boucle », bornée à une lecture système de quelques microsecondes toutes
    /// les trois secondes, et seulement tant que la fonctionnalité tourne.
    /// </summary>
    private static readonly TimeSpan QuietPoll = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan SummaryLifetime = TimeSpan.FromSeconds(12);

    private readonly Func<bool> _isQuiet;
    private readonly QuietSummary _held = new();
    private readonly Timer _quietTimer;
    private bool _quiet;
    private DateTimeOffset? _quietUntil;

    public NotificationFeature(
        IActivityManager activities,
        IEventBus events,
        WindowsNotificationListener listener,
        bool isEnabled = true,
        Func<bool>? isQuiet = null)
        : base(FeatureKey, "Notifications", activities, events, isEnabled)
    {
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _isQuiet = isQuiet ?? (() => false);
        _quietTimer = new Timer(Guarded(RefreshQuiet), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Vrai tant que Windows est en « Ne pas déranger ».</summary>
    public bool IsQuiet
    {
        get
        {
            lock (_gate)
            {
                return _quiet;
            }
        }
    }

    /// <summary>
    /// Silence de réunion (W2) : les notifications sont retenues jusqu'à
    /// <paramref name="until"/>, puis résumées. <c>null</c> lève le silence.
    /// </summary>
    public void QuietUntil(DateTimeOffset? until)
    {
        lock (_gate)
        {
            _quietUntil = until;
        }

        RefreshQuiet();

        // La lune dit jusqu'à quand : on la republie avec son nouveau titre.
        if (until is not null && IsQuiet)
        {
            IslandActivity indicator;

            lock (_gate)
            {
                indicator = QuietIndicator();
            }

            PublishActivity(indicator);
        }
    }

    /// <summary>Fin du silence de réunion en cours, ou <c>null</c>.</summary>
    public DateTimeOffset? QuietEnd
    {
        get
        {
            lock (_gate)
            {
                return _quietUntil;
            }
        }
    }

    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        _listener.NotificationReceived += OnNotificationReceived;
        _quietTimer.Change(TimeSpan.Zero, QuietPoll);

        await _listener.StartAsync().ConfigureAwait(false);
    }

    protected override Task OnStopAsync()
    {
        _listener.NotificationReceived -= OnNotificationReceived;
        _listener.Stop();
        _quietTimer.Change(Timeout.Infinite, Timeout.Infinite);

        lock (_gate)
        {
            _quiet = false;
            _quietUntil = null;
            _held.Clear();
        }

        RemoveActivity(QuietActivityId);

        return Task.CompletedTask;
    }

    protected override void OnDisposed() => _quietTimer.Dispose();

    /// <summary>
    /// Relit l'état « Ne pas déranger ». À l'entrée, la lune s'installe ; à la
    /// sortie, la lune part et, si des notifications ont été retenues, la notch
    /// les résume par application.
    /// </summary>
    public void RefreshQuiet()
    {
        DateTimeOffset? until;

        lock (_gate)
        {
            if (_quietUntil is { } end && end <= DateTimeOffset.Now)
            {
                _quietUntil = null;
            }

            until = _quietUntil;
        }

        bool now = until is not null || _isQuiet();
        IslandActivity? summary = null;
        IReadOnlyList<HeldNotification>? heldItems = null;
        Digest? digest = null;
        bool leaving = false;

        lock (_gate)
        {
            if (now == _quiet)
            {
                return;
            }

            _quiet = now;

            if (!now)
            {
                leaving = true;

                if (_held.Count > 0)
                {
                    // Résumé (I1) : ce qui te concerne d'abord, par des règles locales.
                    heldItems = _held.Items;
                    digest = NotificationDigest.Summarize(heldItems, UserName, Lang.French);
                    summary = QuietSummaryActivity(digest.Headline, QuietSummaryActivityId, ActivityPriority.Normal, ActivityPresentationPolicy.Temporary, SummaryLifetime, digest);
                }

                _held.Clear();
            }
        }

        if (leaving)
        {
            RemoveActivity(QuietActivityId);
        }

        if (summary is not null)
        {
            PublishActivity(summary);

            // Un modèle, s'il est choisi, ajoute une phrase de synthèse par-dessus.
            if (Ask is { } ask && heldItems is not null && digest is not null)
            {
                _ = AddSentenceAsync(ask, summary, heldItems, digest);
            }
        }
    }

    /// <summary>Prénom de l'utilisateur : une notification qui le nomme le concerne.</summary>
    public string? UserName { get; set; }

    /// <summary>Le modèle de langage choisi, ou <c>null</c> : le résumé reste alors fait de règles.</summary>
    public Func<AssistantRequest, Task<string?>>? Ask { get; set; }

    private async Task AddSentenceAsync(Func<AssistantRequest, Task<string?>> ask, IslandActivity summary, IReadOnlyList<HeldNotification> items, Digest digest)
    {
        try
        {
            string? sentence = NotificationDigest.CleanSentence(await ask(NotificationDigest.Prompt(items, UserName, Lang.French)).ConfigureAwait(false));

            // Le résumé a pu partir entre-temps : on ne le fait pas revenir.
            if (sentence is null || !Activities.GetActiveActivities().Any(a => ReferenceEquals(a, summary) || a.Id == QuietSummaryActivityId))
            {
                return;
            }

            IslandActivity updated = QuietSummaryActivity(digest.Headline, QuietSummaryActivityId, ActivityPriority.Normal, ActivityPresentationPolicy.Temporary, SummaryLifetime, digest, sentence, summary.Payload as QuietPayload);
            PublishActivity(updated);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    /// <summary>
    /// Une notification arrivée. Appelable directement, ce qui rend la
    /// fonctionnalité testable sans l'écouteur de Windows.
    /// </summary>
    public void Receive(string appName, string title, string body, byte[]? logo = null)
        => OnNotificationReceived(appName, title, body, logo);

    /// <summary>
    /// Une autre fonctionnalité qui comprend certaines notifications (le
    /// téléphone) : si elle rend <c>true</c>, la notification lui appartient.
    /// </summary>
    public Func<string, string, string, bool>? Intercept { get; set; }

    /// <summary>Applications dont les notifications restent dans le coin de l'écran.</summary>
    public IReadOnlyCollection<string> IgnoredApps { get; set; } = [];

    /// <summary>État de l'accès aux notifications Windows, pour les réglages et la présentation.</summary>
    public static NotificationAccess Access => WindowsNotificationListener.GetAccess();

    /// <summary>Demande l'accès (fenêtre de Windows), puis écoute s'il est accordé.</summary>
    public Task<NotificationAccess> RequestAccessAsync() => _listener.RequestAccessAsync();

    private void OnNotificationReceived(string appName, string title, string body, byte[]? logo)
    {
        if (IgnoredApps.Contains(appName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        // Un appel ou une livraison (T1, T2) a sa propre carte : pas de doublon.
        if (Intercept?.Invoke(appName, title, body) == true)
        {
            return;
        }

        // Une notification rejoint le groupe de son application : l'activité du
        // groupe est republiée — même identifiant — au lieu d'en empiler une de
        // plus. Sa durée de vie repart à chaque arrivée.
        IslandActivity activity;

        lock (_gate)
        {
            if (_quiet)
            {
                // Au calme, rien ne s'affiche : la notification est comptée, et
                // la lune porte le compte.
                _held.Hold(appName, title, body, DateTimeOffset.UtcNow);
                activity = QuietIndicator();
            }
            else
            {
                activity = _groups.Add(FeatureKey, appName, title, body, DateTimeOffset.UtcNow, logo);
            }
        }

        PublishActivity(activity);

        if (activity.Id != QuietActivityId)
        {
            PublishEvent(new NotificationPostedEvent(appName, title, body));
        }
    }

    /// <summary>La lune, discrète : le nombre de notifications retenues à droite ; ouverte, le résumé en cours.</summary>
    private IslandActivity QuietIndicator()
        => QuietSummaryActivity(
            _quietUntil is { } until ? SpaceNotch.Core.Calendar.MeetingQuiet.Label(until, Lang.French) : Lang.T("Ne pas déranger", "Do not disturb"),
            QuietActivityId,
            ActivityPriority.Normal,
            ActivityPresentationPolicy.Passive,
            null);

    private IslandActivity QuietSummaryActivity(
        string title,
        string id,
        ActivityPriority priority,
        ActivityPresentationPolicy policy,
        TimeSpan? duration,
        Digest? digest = null,
        string? sentence = null,
        QuietPayload? previous = null)
    {
        IReadOnlyList<QuietGroup> groups = previous?.Groups ?? _held.Groups();
        int total = previous?.Total ?? _held.Count;

        return new IslandActivity
        {
            Id = id,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Quiet,
            Title = title,
            Subtitle = sentence ?? (total == 0
                ? Lang.T("Les notifications attendent", "Notifications will wait")
                : digest is not null
                    ? Lang.T("Pendant le calme", "While you were away")
                    : Lang.Count(total, "notification retenue", "notifications retenues", "notification held", "notifications held")),
            Source = Lang.T("Ne pas déranger", "Do not disturb"),
            Metric = total > 0 ? total.ToString(System.Globalization.CultureInfo.CurrentCulture) : null,
            IconKey = "Moon",
            State = IslandActivityState.Notification,
            Priority = priority,
            Policy = policy,
            Duration = duration,
            Payload = new QuietPayload(groups, total, digest)
        };
    }
}
