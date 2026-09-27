using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
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

    /// <summary>Ne pas déranger (F9) : l'activité discrète — la lune — tant que Windows est au calme.</summary>
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
        _quietTimer = new Timer(_ => RefreshQuiet(), null, Timeout.Infinite, Timeout.Infinite);
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
        bool now = _isQuiet();
        IslandActivity? indicator = null;
        IslandActivity? summary = null;
        bool leaving = false;

        lock (_gate)
        {
            if (now == _quiet)
            {
                return;
            }

            _quiet = now;

            if (now)
            {
                indicator = QuietIndicator();
            }
            else
            {
                leaving = true;
                summary = _held.Count > 0 ? QuietSummaryActivity(Lang.T("Pendant le calme", "While you were away"), QuietSummaryActivityId, ActivityPriority.Normal, ActivityPresentationPolicy.Temporary, SummaryLifetime) : null;
                _held.Clear();
            }
        }

        if (indicator is not null)
        {
            PublishActivity(indicator);
        }

        if (leaving)
        {
            RemoveActivity(QuietActivityId);
        }

        if (summary is not null)
        {
            PublishActivity(summary);
        }
    }

    /// <summary>
    /// Une notification arrivée. Appelable directement, ce qui rend la
    /// fonctionnalité testable sans l'écouteur de Windows.
    /// </summary>
    public void Receive(string appName, string title, string body, byte[]? logo = null)
        => OnNotificationReceived(appName, title, body, logo);

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
                _held.Hold(appName, string.IsNullOrWhiteSpace(title) ? body : title, DateTimeOffset.UtcNow);
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
        => QuietSummaryActivity(Lang.T("Ne pas déranger", "Do not disturb"), QuietActivityId, ActivityPriority.Background, ActivityPresentationPolicy.Passive, null);

    private IslandActivity QuietSummaryActivity(
        string title,
        string id,
        ActivityPriority priority,
        ActivityPresentationPolicy policy,
        TimeSpan? duration)
    {
        IReadOnlyList<QuietGroup> groups = _held.Groups();
        int total = _held.Count;

        return new IslandActivity
        {
            Id = id,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Quiet,
            Title = title,
            Subtitle = total == 0
                ? Lang.T("Les notifications attendent", "Notifications will wait")
                : Lang.T($"{total} notification(s) retenue(s)", $"{total} notification(s) held"),
            Source = Lang.T("Ne pas déranger", "Do not disturb"),
            Metric = total > 0 ? total.ToString(System.Globalization.CultureInfo.CurrentCulture) : null,
            IconKey = "Moon",
            State = IslandActivityState.Notification,
            Priority = priority,
            Policy = policy,
            Duration = duration,
            Payload = new QuietPayload(groups, total)
        };
    }
}
