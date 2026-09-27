using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Platform.Windows.Setup;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace SpaceNotch.Platform.Windows.Notifications;

/// <summary>Où en est l'accès aux notifications Windows.</summary>
public enum NotificationAccess
{
    /// <summary>Pas d'identité de paquet (exécutable portable) : Windows refuse l'écoute.</summary>
    Unavailable = 0,

    /// <summary>Jamais demandé : la présentation ou les réglages le demanderont.</summary>
    NotAsked,

    /// <summary>Refusé dans les paramètres de confidentialité de Windows.</summary>
    Denied,

    Allowed
}

/// <summary>
/// Écouteur de notifications Windows (UserNotificationListener).
///
/// <para>
/// L'accès n'est plus demandé au démarrage — une fenêtre système surgissait
/// sans explication — mais depuis la présentation du premier lancement ou les
/// réglages, là où l'on dit à quoi il sert. Au démarrage, on n'écoute que si
/// l'accès est déjà accordé.
/// </para>
///
/// <para>
/// L'événement <c>NotificationChanged</c> n'est pas garanti hors d'une
/// application UWP (« Élément introuvable ») : s'il est refusé, la liste est
/// relue toutes les deux secondes et seules les nouvelles notifications sont
/// annoncées.
/// </para>
/// </summary>
public sealed class WindowsNotificationListener : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly HashSet<uint> _seen = [];
    private readonly Lock _gate = new();

    private UserNotificationListener? _listener;
    private Timer? _poll;
    private int _polling;

    /// <summary>
    /// Une notification est arrivée : application, titre, corps, et l'icône de
    /// l'application en PNG quand Windows la fournit.
    /// </summary>
    public event Action<string, string, string, byte[]?>? NotificationReceived;

    /// <summary>Icônes déjà lues, par application : une seule lecture par application.</summary>
    private readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, byte[]?> _logos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Diagnostic.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Vrai tant que les notifications système sont écoutées.</summary>
    public bool IsListening { get; private set; }

    /// <summary>État de l'accès, sans rien demander.</summary>
    public static NotificationAccess GetAccess()
    {
        if (!IdentityPackage.HasIdentity)
        {
            return NotificationAccess.Unavailable;
        }

        try
        {
            return UserNotificationListener.Current.GetAccessStatus() switch
            {
                UserNotificationListenerAccessStatus.Allowed => NotificationAccess.Allowed,
                UserNotificationListenerAccessStatus.Denied => NotificationAccess.Denied,
                _ => NotificationAccess.NotAsked
            };
        }
        catch (Exception)
        {
            return NotificationAccess.Unavailable;
        }
    }

    /// <summary>
    /// Demande l'accès à l'utilisateur (fenêtre de Windows). À appeler depuis le
    /// fil de l'interface, en réponse à un geste. Écoute aussitôt si c'est accordé.
    /// </summary>
    public async Task<NotificationAccess> RequestAccessAsync()
    {
        if (!IdentityPackage.HasIdentity)
        {
            return NotificationAccess.Unavailable;
        }

        try
        {
            UserNotificationListenerAccessStatus status = await UserNotificationListener.Current.RequestAccessAsync();

            if (status == UserNotificationListenerAccessStatus.Allowed)
            {
                await StartAsync().ConfigureAwait(false);
                return NotificationAccess.Allowed;
            }

            return status == UserNotificationListenerAccessStatus.Denied ? NotificationAccess.Denied : NotificationAccess.NotAsked;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[NOTIFICATIONS] Demande d'accès impossible : {ex.Message}");
            return NotificationAccess.Unavailable;
        }
    }

    /// <summary>Écoute si l'accès est déjà accordé ; ne demande jamais rien.</summary>
    public async Task StartAsync()
    {
        if (IsListening || GetAccess() != NotificationAccess.Allowed)
        {
            return;
        }

        _listener = UserNotificationListener.Current;

        // Ce qui est déjà dans le centre de notifications n'est pas nouveau.
        await RememberExistingAsync().ConfigureAwait(false);

        try
        {
            _listener.NotificationChanged += OnNotificationChanged;
            Log?.Invoke("[NOTIFICATIONS] Écoute par événement.");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[NOTIFICATIONS] Événement refusé ({ex.Message}) : relecture toutes les 2 s.");
            _poll = new Timer(_ => _ = PollAsync(), null, PollInterval, PollInterval);
        }

        IsListening = true;
    }

    /// <summary>
    /// Cesse d'écouter. Le consentement déjà accordé reste acquis : une
    /// réactivation n'a pas à le redemander.
    /// </summary>
    public void Stop()
    {
        if (!IsListening)
        {
            return;
        }

        _poll?.Dispose();
        _poll = null;

        try
        {
            if (_listener is not null)
            {
                _listener.NotificationChanged -= OnNotificationChanged;
            }
        }
        catch
        {
            // Abonnement jamais accepté : rien à retirer.
        }

        IsListening = false;
    }

    public void Dispose() => Stop();

    private async Task RememberExistingAsync()
    {
        try
        {
            IReadOnlyList<UserNotification> existing = await _listener!.GetNotificationsAsync(NotificationKinds.Toast);

            lock (_gate)
            {
                foreach (UserNotification notification in existing)
                {
                    _seen.Add(notification.Id);
                }
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[NOTIFICATIONS] Lecture initiale impossible : {ex.Message}");
        }
    }

    private async Task PollAsync()
    {
        if (_listener is null || Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            IReadOnlyList<UserNotification> current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            List<UserNotification> fresh;

            lock (_gate)
            {
                fresh = current.Where(n => _seen.Add(n.Id)).ToList();

                // Oublier ce qui a quitté le centre de notifications : l'ensemble
                // ne grossit pas sans fin.
                _seen.IntersectWith(current.Select(n => n.Id));
            }

            foreach (UserNotification notification in fresh)
            {
                _ = AnnounceAsync(notification);
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[NOTIFICATIONS] Relecture impossible : {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind != UserNotificationChangedKind.Added)
        {
            return;
        }

        lock (_gate)
        {
            if (!_seen.Add(args.UserNotificationId))
            {
                return;
            }
        }

        try
        {
            if (sender.GetNotification(args.UserNotificationId) is { } notification)
            {
                _ = AnnounceAsync(notification);
            }
        }
        catch
        {
            // Notification déjà expirée.
        }
    }

    private async Task AnnounceAsync(UserNotification notification)
    {
        try
        {
            NotificationBinding? binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);

            if (binding is null)
            {
                return;
            }

            List<AdaptiveNotificationText> texts = binding.GetTextElements().ToList();
            string title = texts.Count > 0 ? texts[0].Text : "Notification";
            string body = texts.Count > 1 ? texts[1].Text : string.Empty;
            string appName = notification.AppInfo?.DisplayInfo?.DisplayName ?? "Windows";
            byte[]? logo = await LogoAsync(notification, appName).ConfigureAwait(false);

            NotificationReceived?.Invoke(appName, title, body, logo);
        }
        catch
        {
            // Notification au format inattendu : ignorée.
        }
    }

    /// <summary>
    /// Icône de l'application qui notifie, lue une fois puis gardée : la notch
    /// montre le vrai logo de Discord ou de Teams, et non une cloche générique.
    /// </summary>
    private async Task<byte[]?> LogoAsync(UserNotification notification, string appName)
    {
        if (_logos.TryGetValue(appName, out byte[]? cached))
        {
            return cached;
        }

        byte[]? bytes = null;

        try
        {
            if (notification.AppInfo?.DisplayInfo is { } info)
            {
                global::Windows.Storage.Streams.RandomAccessStreamReference reference = info.GetLogo(new global::Windows.Foundation.Size(64, 64));
                using global::Windows.Storage.Streams.IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();

                if (stream.Size is > 0 and < 4 * 1024 * 1024)
                {
                    using var reader = new global::Windows.Storage.Streams.DataReader(stream);
                    await reader.LoadAsync((uint)stream.Size);
                    bytes = new byte[stream.Size];
                    reader.ReadBytes(bytes);
                }
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[NOTIFICATIONS] Icône de {appName} illisible : {ex.Message}");
        }

        _logos[appName] = bytes;
        return bytes;
    }
}
