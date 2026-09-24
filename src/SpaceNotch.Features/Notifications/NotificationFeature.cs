using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
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
    /// Durée de vie d'une notification dans l'Island. C'est elle qui évite
    /// l'accumulation : sans durée, chaque notification resterait indéfiniment
    /// dans le gestionnaire d'activités.
    /// </summary>
    private static readonly TimeSpan NotificationLifetime = TimeSpan.FromSeconds(4);

    private readonly WindowsNotificationListener _listener;

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

    private void OnNotificationReceived(string appName, string title, string body)
    {
        // Chaque notification est un événement distinct : identifiant unique, mais
        // durée de vie explicite pour garantir sa disparition.
        var activity = new IslandActivity
        {
            Id = $"notification.{Guid.NewGuid():N}",
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Notification,
            Title = title,
            Subtitle = body,
            Source = appName,
            IconKey = "Notification",
            State = IslandActivityState.Notification,
            Priority = ActivityPriority.High,
            Duration = NotificationLifetime,
            Payload = (AppName: appName, Title: title, Body: body)
        };

        PublishActivity(activity);
        PublishEvent(new NotificationPostedEvent(appName, title, body));
    }
}
