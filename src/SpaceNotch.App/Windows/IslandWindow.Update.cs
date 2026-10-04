using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using SpaceNotch.Core.Setup;
using SpaceNotch.Core.State;
using SpaceNotch.Core.Update;
using SpaceNotch.Features.Update;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Setup;
using SpaceNotch.Platform.Windows.Update;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Mise à jour automatique (v1.17.0).
///
/// <list type="bullet">
/// <item>Deux minutes après le démarrage, puis toutes les six heures, la
/// dernière version publiée sur GitHub est lue.</item>
/// <item>Plus récente : l'installeur est téléchargé dans
/// <c>%LocalAppData%\SpaceNotch\updates</c> et vérifié par son SHA-256,
/// publié avec la version.</item>
/// <item>« Automatiques » : installée en silence au premier moment calme —
/// trois minutes sans souris ni clavier, notch au repos — puis la notch
/// redémarre et dit « Mise à jour faite ». « Me prévenir » : une carte
/// propose Installer ou Plus tard.</item>
/// </list>
/// </summary>
public sealed partial class IslandWindow
{
    private UpdateFeature? _updateFeature;
    private UpdateClient? _updateClient;
    private DispatcherQueueTimer? _updateCheckTimer;
    private DispatcherQueueTimer? _updateQuietTimer;
    private DownloadedUpdate? _pendingUpdate;
    private bool _updateChecking;
    private bool _updateInstalling;
    private DateTimeOffset? _updateOfferedAt;

    private static Version CurrentVersion
        => UpdateRules.Normalize(typeof(IslandWindow).Assembly.GetName().Version ?? new Version(1, 0, 0));

    private UpdateFeature CreateUpdateFeature()
    {
        _updateFeature = new UpdateFeature(_activityManager, _eventBus, _settings.IsFeatureEnabled(UpdateFeature.FeatureKey));
        _updateFeature.InstallRequested += () => OnUiThread(() => _ = InstallUpdateAsync());
        _updateFeature.LaterRequested += () => OnUiThread(PostponeUpdate);
        _updateFeature.NotesRequested += () => OnUiThread(OpenUpdateNotes);
        return _updateFeature;
    }

    /// <summary>Au démarrage : « Mise à jour faite » si la version a changé, puis la surveillance.</summary>
    private void StartUpdates()
    {
        Version current = CurrentVersion;

        // Une copie qui n'est pas celle installée (build de développement) ne
        // touche ni à l'installation ni à la version mémorisée dans les réglages,
        // partagés avec elle.
        if (WindowsSetup.FindInstalled() is { } installed && !UpdateRules.MayUpdateItself(Environment.ProcessPath, installed.Executable))
        {
            MiniLogger.Log($"[MISE À JOUR] Copie hors installation ({Environment.ProcessPath}) : pas de mise à jour");
            return;
        }

        if (UpdateRules.JustUpdated(_settings.LastRunVersion, current))
        {
            MiniLogger.Log($"[MISE À JOUR] Version {UpdateRules.Display(current)} en service (avant : {_settings.LastRunVersion})");
            RunAfter(TimeSpan.FromSeconds(4), () => _updateFeature?.ShowUpdated(current));
        }

        if (_settings.LastRunVersion != UpdateRules.Display(current))
        {
            _settingsService.Update(s => s.LastRunVersion = UpdateRules.Display(current));
        }

        _updateClient = new UpdateClient(UpdateRules.Display(current));
        _updateCheckTimer = CreateOneShotTimer(UpdateRules.FirstCheckDelay, () => _ = CheckForUpdateAsync());
        _updateCheckTimer.Start();
    }

    private async Task CheckForUpdateAsync()
    {
        // La suivante est armée d'abord : une vérification ratée n'arrête pas la surveillance.
        _updateCheckTimer!.Interval = UpdateRules.CheckInterval;
        _updateCheckTimer.Start();

        if (_isClosed || _touring || _updateChecking || _updateInstalling || _settings.UpdateMode == UpdateMode.Off)
        {
            return;
        }

        _updateChecking = true;

        try
        {
            ReleaseInfo? latest = await _updateClient!.LatestAsync();

            if (latest is null || !UpdateRules.IsNewer(latest.Version, CurrentVersion))
            {
                MiniLogger.Log($"[MISE À JOUR] À jour ({UpdateRules.Display(CurrentVersion)})");
                return;
            }

            if (_pendingUpdate?.Release.Version == latest.Version)
            {
                ConsiderUpdate();
                return;
            }

            MiniLogger.Log($"[MISE À JOUR] {latest.Tag} disponible : téléchargement");
            DownloadedUpdate? downloaded = await _updateClient.DownloadAsync(latest, MiniLogger.Log);

            if (downloaded is null || _isClosed)
            {
                return;
            }

            _pendingUpdate = downloaded;
            MiniLogger.Log($"[MISE À JOUR] {latest.Tag} prête ({downloaded.SetupPath})");

            _updateQuietTimer ??= CreateRepeatingTimer(TimeSpan.FromMinutes(1), ConsiderUpdate);
            _updateQuietTimer.Start();
            ConsiderUpdate();
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[MISE À JOUR] vérification impossible", ex);
        }
        finally
        {
            _updateChecking = false;
        }
    }

    /// <summary>Chaque minute, une mise à jour prête : l'installer, la proposer, ou attendre.</summary>
    private void ConsiderUpdate()
    {
        if (_pendingUpdate is not { } update || _isClosed || _touring || _updateInstalling)
        {
            return;
        }

        bool installed = WindowsSetup.FindInstalled() is not null;
        bool busy = _controller.State is IslandState.Expanded or IslandState.Expanding or IslandState.Preview
            || (_controller.PresentedActivity is { } shown && shown.FeatureId != UpdateFeature.FeatureKey);

        UpdateAction action = UpdateRules.Decide(
            _settings.UpdateMode,
            downloaded: true,
            installed,
            LastInputIdle(),
            busy,
            _settings.UpdatePostponedUntil,
            DateTimeOffset.Now);

        switch (action)
        {
            case UpdateAction.Install:
                _ = InstallUpdateAsync();
                break;

            case UpdateAction.Offer:
                // Une seule proposition par période de report : ignorée, elle
                // vaut « Plus tard » au lieu de revenir chaque minute.
                if (_updateOfferedAt is { } offered && DateTimeOffset.Now - offered < UpdateRules.Postpone)
                {
                    break;
                }

                if (!_activityManager.GetActiveActivities().Any(a => a.Id == UpdateFeature.ActivityId))
                {
                    _updateOfferedAt = DateTimeOffset.Now;
                    _updateFeature?.ShowReady(update.Release.Version, installed);
                }

                break;

            case UpdateAction.None:
                _updateFeature?.Clear();
                break;
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is not { } update || _updateInstalling || _isClosed)
        {
            return;
        }

        if (WindowsSetup.FindInstalled() is not { } product)
        {
            // Version portable : rien à remplacer, la page de la version s'ouvre.
            OpenUpdateNotes();
            return;
        }

        _updateInstalling = true;

        try
        {
            if (!await UpdateClient.VerifyAsync(update.SetupPath, update.Sha256))
            {
                MiniLogger.Log("[MISE À JOUR] installeur altéré depuis le téléchargement : abandon");
                _pendingUpdate = null;
                _updateFeature?.Clear();
                return;
            }

            _updateFeature?.ShowInstalling(update.Release.Version);

            if (!UpdateClient.StartInstall(update.SetupPath, product.Options, MiniLogger.Log))
            {
                _updateFeature?.Clear();
                return;
            }

            // L'installeur arrête la notch de toute façon ; partir de soi-même
            // laisse l'arrêt propre (raccourcis libérés, réglages écrits).
            MiniLogger.Log($"[MISE À JOUR] installation de {update.Release.Tag} lancée : la notch se ferme");
            RunAfter(TimeSpan.FromSeconds(1.5), QuitApplication);
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[MISE À JOUR] installation impossible", ex);
            _updateFeature?.Clear();
        }
        finally
        {
            _updateInstalling = false;
        }
    }

    private void PostponeUpdate()
    {
        _settingsService.Update(s => s.UpdatePostponedUntil = DateTimeOffset.Now + UpdateRules.Postpone);
        _updateFeature?.Clear();
        MiniLogger.Log("[MISE À JOUR] reportée de 20 h");
    }

    private void OpenUpdateNotes()
    {
        string url = _pendingUpdate?.Release.PageUrl
            ?? $"https://github.com/{UpdateRules.Repository}/releases/tag/v{UpdateRules.Display(CurrentVersion)}";

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[MISE À JOUR] page de la version impossible à ouvrir", ex);
        }

        _updateFeature?.Clear();
    }

    private void StopUpdates()
    {
        _updateCheckTimer?.Stop();
        _updateQuietTimer?.Stop();
        _updateClient?.Dispose();
        _updateClient = null;
    }
}
