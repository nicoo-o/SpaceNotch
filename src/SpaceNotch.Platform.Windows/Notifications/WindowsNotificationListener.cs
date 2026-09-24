using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace SpaceNotch.Platform.Windows.Notifications;

/// <summary>
/// Écouteur de notifications Windows (UserNotificationListener).
/// 100% événementiel, capture les toasts entrants pour affichage dans l'Island.
/// </summary>
public sealed class WindowsNotificationListener
{
    private UserNotificationListener? _listener;

    public event Action<string, string, string>? NotificationReceived;

    /// <summary>Vrai tant que les notifications système sont écoutées.</summary>
    public bool IsListening { get; private set; }

    public async Task StartAsync()
    {
        if (IsListening)
        {
            return;
        }

        try
        {
            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();

            if (access == UserNotificationListenerAccessStatus.Allowed)
            {
                _listener.NotificationChanged += OnNotificationChanged;
                IsListening = true;
            }
        }
        catch
        {
            // Tolérance selon les permissions utilisateur / stratégies de groupe
        }
    }

    /// <summary>
    /// Cesse d'écouter les notifications. Le consentement utilisateur déjà accordé
    /// reste acquis : une réactivation n'a pas à le redemander.
    /// </summary>
    public void Stop()
    {
        if (!IsListening || _listener is null)
        {
            return;
        }

        try
        {
            _listener.NotificationChanged -= OnNotificationChanged;
        }
        catch
        {
            // Tolérance.
        }

        IsListening = false;
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind != UserNotificationChangedKind.Added)
        {
            return;
        }

        try
        {
            var notif = sender.GetNotification(args.UserNotificationId);
            if (notif == null) return;

            var binding = notif.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding == null) return;

            var textElements = binding.GetTextElements().ToList();
            string title = textElements.Count > 0 ? textElements[0].Text : "Notification";
            string body = textElements.Count > 1 ? textElements[1].Text : string.Empty;
            string appName = notif.AppInfo?.DisplayInfo?.DisplayName ?? "Windows";

            NotificationReceived?.Invoke(appName, title, body);
        }
        catch
        {
            // Tolérance en cas de notification expirée
        }
    }
}
