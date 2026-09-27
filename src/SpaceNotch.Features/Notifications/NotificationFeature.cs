using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
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

    public NotificationFeature(
        IActivityManager activities,
        IEventBus events,
        WindowsNotificationListener listener,
        bool isEnabled = true)
        : base(FeatureKey, "Notifications", activities, events, isEnabled)
    {
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
    }

    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        _listener.NotificationReceived += OnNotificationReceived;

        await _listener.StartAsync().ConfigureAwait(false);
    }

    protected override Task OnStopAsync()
    {
        _listener.NotificationReceived -= OnNotificationReceived;
        _listener.Stop();

        return Task.CompletedTask;
    }

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
            activity = _groups.Add(FeatureKey, appName, title, body, DateTimeOffset.UtcNow, logo);
        }

        PublishActivity(activity);
        PublishEvent(new NotificationPostedEvent(appName, title, body));
    }
}
