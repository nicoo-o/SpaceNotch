using System;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Features.Clipboard;
using SpaceNotch.Features.FileShelf;
using SpaceNotch.Features.Menu;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Menu rapide : clic droit sur la notch. La fonctionnalité présente le menu ;
/// ses commandes — détacher, accrocher, réglages, quitter — touchent la
/// fenêtre, et c'est ici qu'elles sont exécutées.
/// </summary>
public sealed partial class IslandWindow
{
    /// <summary>Le temps que la notch se replie avant de s'arracher du bord.</summary>
    private static readonly TimeSpan DetachAfterCollapse = TimeSpan.FromMilliseconds(260);

    private DispatcherTimer? _menuDetachTimer;

    /// <summary>Clic droit : ouvre le menu, ou le referme s'il est déjà là.</summary>
    private void ToggleQuickMenu()
    {
        if (_isClosed)
        {
            return;
        }

        if (_quickMenuFeature.IsShown && _controller.State != IslandState.Closed)
        {
            CollapseByUser("menu rapide");
            return;
        }

        _quickMenuFeature.Show(new QuickMenuPayload(
            Hotkey: _launcherFeature.Hotkey,
            DockExpanded: false,
            Edge: _edge,
            IsFloating: UsesFloatingGeometry,
            SideEdgesAllowed: _settings.AllowSideEdges,
            HasClipboard: HasActivity(ClipboardFeature.ActivityId),
            HasShelf: HasActivity(FileShelfManager.ShelfActivityId),
            TimerRunning: _timerFeature.IsMeasuring));

        // Le menu passe devant ce qui était présenté — la musique, un minuteur —
        // le temps d'être utilisé ; il rend la main en se refermant.
        _activityManager.PinPresentation(QuickMenuFeature.ActivityId);
        RevealPresented();

        // Après la prise en compte de l'ouverture : la goutte part de la forme qui s'ouvre.
        _dispatcherQueue.TryEnqueueSafely(DispatcherQueuePriority.Low, Drip);
    }

    private DispatcherQueueTimer? _dripTimer;

    /// <summary>
    /// Menu liquide (U1) : la notch s'étire d'abord en goutte — étroite et
    /// longue, comme de l'encre qui coule — puis s'élargit et devient le menu.
    /// Les lignes arrivent ensuite en cascade.
    /// </summary>
    private void Drip()
    {
        if (!UseSpringAnimations() || UsesFloatingGeometry || UsesSideTab)
        {
            return;
        }

        IslandFootprint menu = IslandSceneCatalog.FootprintFor(IslandSceneCatalog.QuickMenu);
        var drop = new IslandFootprint(Math.Max(_restFootprint.Width * 0.9, 120), menu.Height * 0.62);
        _controller.Via(drop);

        _dripTimer ??= CreateOneShotTimer(TimeSpan.FromMilliseconds(150), _controller.Resume);
        _dripTimer.Stop();
        _dripTimer.Start();
    }

    private bool HasActivity(string activityId)
        => _activityManager.GetActiveActivities().Any(a => a.Id == activityId);

    /// <summary>Vrai si la demande était une commande du menu, exécutée ici.</summary>
    private bool HandleQuickMenuAction(IslandActionRequest request)
    {
        switch (request.ActionId)
        {
            case QuickMenuTiles.TileAction:
                // Tableau de bord (ADR-028) : la recherche cède la place à la
                // commande de la tuile, exécutée comme depuis le menu rapide.
                if (QuickMenuTiles.At(request.Value) is not { } tile)
                {
                    return true;
                }

                _launcherFeature.Dismiss();

                if (tile.ActionId == QuickMenuFeature.MoreAction)
                {
                    ToggleQuickMenu();
                    return true;
                }

                // Un minuteur qui tourne est montré, pas relancé à 15 min : le menu
                // propose alors « Arrêter », la tuile ne doit pas l'écraser.
                if (tile.ActionId == QuickMenuFeature.TimerAction && _timerFeature.IsMeasuring)
                {
                    PresentFromMenu(SpaceNotch.Features.Productivity.TimerFeature.ActivityId);
                    return true;
                }

                // Une commande inconnue ne repart pas vers les fonctionnalités : le
                // lanceur est déjà fermé, et la requête d'origine n'a pas de destinataire.
                HandleQuickMenuAction(request with { ActionId = tile.ActionId, Value = tile.Value });
                return true;

            case QuickMenuFeature.SearchAction:
                CloseQuickMenu();
                OpenLauncher();
                return true;

            case QuickMenuFeature.TimerAction:
                CloseQuickMenu();

                if (request.Value == "0")
                {
                    _timerFeature.Reset();
                    CollapseByUser("menu rapide");
                    return true;
                }

                int minutes = int.TryParse(request.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 15;
                _timerFeature.StartCountdown(TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 180)));
                RevealPresented();
                return true;

            case QuickMenuFeature.ClipboardAction:
                PresentFromMenu(ClipboardFeature.ActivityId);
                return true;

            case QuickMenuFeature.ShelfAction:
                PresentFromMenu(FileShelfManager.ShelfActivityId);
                return true;

            case QuickMenuFeature.NoteAction:
                OpenNote();
                return true;

            case QuickMenuFeature.DetachAction:
                CloseQuickMenu();
                CollapseByUser("menu rapide");
                ScheduleDetachFromMenu();
                return true;

            case QuickMenuFeature.DockAction:
                CloseQuickMenu();
                CollapseByUser("menu rapide");
                DockFromMenu(Enum.TryParse(request.Value, out NotchEdge edge) ? edge : NotchEdge.Top);
                return true;

            case QuickMenuFeature.SettingsAction:
                CloseQuickMenu();
                CollapseByUser("menu rapide");
                OpenSettingsWindow();
                return true;

            case QuickMenuFeature.QuitAction:
                QuitApplication();
                return true;

            default:
                return false;
        }
    }

    /// <summary>Retire le menu et rend l'arbitrage automatique à la pile.</summary>
    private void CloseQuickMenu()
    {
        _quickMenuFeature.Dismiss();
        _activityManager.PinPresentation(null);
    }

    /// <summary>Presse-papier, étagère : le menu cède la place à l'activité demandée.</summary>
    private void PresentFromMenu(string activityId)
    {
        if (HasActivity(activityId))
        {
            // L'épingle avant de retirer le menu : au repos, une entrée de la pile
            // seulement (le presse-papier) ne prend pas la tête d'elle-même ; menu
            // retiré d'abord, la notch se repliait un instant avant de rouvrir.
            _activityManager.PinPresentation(activityId);
            _quickMenuFeature.Dismiss();
            RevealPresented();
        }
        else
        {
            _quickMenuFeature.Dismiss();
            _activityManager.PinPresentation(null);
            CollapseByUser("menu rapide");
        }
    }

    /// <summary>
    /// « Accrocher à… » : détachée, la notch vole jusqu'au bord choisi ;
    /// accrochée, elle change de bord comme depuis les réglages.
    /// </summary>
    private void DockFromMenu(NotchEdge edge)
    {
        if (!_settings.AllowSideEdges)
        {
            edge = NotchEdge.Top;
        }

        if (UsesFloatingGeometry)
        {
            ReattachTo(edge, 0.5);
            return;
        }

        if (edge == _edge)
        {
            return;
        }

        _settingsService.Update(s =>
        {
            s.DockEdge = edge;
            s.DockOffset = 0.5;
        });
    }

    private void ScheduleDetachFromMenu()
    {
        _menuDetachTimer?.Stop();
        _menuDetachTimer = new DispatcherTimer { Interval = DetachAfterCollapse };
        _menuDetachTimer.Tick += SpaceNotch_App.Diagnostics.Guard.XamlTick((_, _) =>
        {
            _menuDetachTimer?.Stop();
            DetachFromMenu();
        });
        _menuDetachTimer.Start();
    }

    /// <summary>
    /// « Détacher de l'écran » : le geste d'arrachement, joué sans la main —
    /// la notch cède, puis est lancée loin du bord et se pose en pastille.
    /// </summary>
    private void DetachFromMenu()
    {
        if (_isClosed || UsesFloatingGeometry || _pointerDown || _dragPhase != DragPhase.None)
        {
            return;
        }

        if (!_settings.AllowDetach)
        {
            MiniLog("détachement refusé : désactivé dans les réglages");
            return;
        }

        ScreenRect pill = CurrentPillRect();
        _pressX = pill.CenterX;
        _pressY = pill.CenterY;

        BeginTear();

        // Lancée perpendiculairement au bord, assez fort pour ne pas y être
        // ramenée par l'aimant.
        (double X, double Y) velocity = _edge switch
        {
            NotchEdge.Left => (1400, 0),
            NotchEdge.Right => (-1400, 0),
            _ => (0, 1400)
        };

        Release(velocity);
        HookDetachFrames();
    }
}
