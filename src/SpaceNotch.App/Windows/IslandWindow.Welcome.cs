using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using SpaceNotch.Core.Activities;
using SpaceNotch.Features.Menu;
using SpaceNotch.Features.Notifications;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Notifications;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Présentation du premier lancement : la notch s'ouvre d'elle-même sur ses
/// cinq gestes, une seule fois. « Revoir la présentation » la rejoue depuis
/// Réglages › Général.
/// </summary>
public sealed partial class IslandWindow
{
    /// <summary>Le temps que la notch se soit posée et que les fonctionnalités aient démarré.</summary>
    private static readonly TimeSpan WelcomeDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>Premier lancement : présente la notch si ce n'est pas déjà fait.</summary>
    public void OfferWelcome()
    {
        if (_settings.WelcomeCompleted)
        {
            return;
        }

        var timer = new DispatcherTimer { Interval = WelcomeDelay };
        timer.Tick += SpaceNotch_App.Diagnostics.Guard.XamlTick((_, _) =>
        {
            timer.Stop();
            ShowWelcome();
        });
        timer.Start();
    }

    /// <summary>Ouvre la présentation, depuis le début.</summary>
    public void ShowWelcome()
    {
        if (_isClosed)
        {
            return;
        }

        _welcomeFeature.Show(AccessKey(NotificationFeature.Access));
        _activityManager.PinPresentation(WelcomeFeature.ActivityId);
        RevealPresented();
        MiniLogger.Log("Présentation du premier lancement ouverte");
    }

    private async Task<bool> HandleWelcomeActionAsync(IslandActionRequest request)
    {
        if (request.ActionId != WelcomeFeature.AllowAction)
        {
            return false;
        }

        // La demande part du fil d'interface, en réponse au clic : c'est ce que
        // Windows exige pour afficher sa fenêtre de consentement.
        NotificationAccess access = await _notificationFeature.RequestAccessAsync();
        MiniLogger.Log($"Accès aux notifications : {access}");

        _welcomeFeature.UpdateAccess(AccessKey(access));
        _welcomeFeature.Finish();
        return true;
    }

    private void OnWelcomeCompleted()
    {
        _activityManager.PinPresentation(null);

        if (!_settings.WelcomeCompleted)
        {
            _settingsService.Update(s => s.WelcomeCompleted = true);
        }

        CollapseByUser("présentation");
    }

    private static string AccessKey(NotificationAccess access) => access switch
    {
        NotificationAccess.Allowed => "allowed",
        NotificationAccess.Denied => "denied",
        NotificationAccess.NotAsked => "notasked",
        _ => "unavailable"
    };
}
