using System;
using System.Globalization;
using System.Linq;
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
            _controller.RequestCollapse();
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
    }

    private bool HasActivity(string activityId)
        => _activityManager.GetActiveActivities().Any(a => a.Id == activityId);

    /// <summary>Vrai si la demande était une commande du menu, exécutée ici.</summary>
    private bool HandleQuickMenuAction(IslandActionRequest request)
    {
        switch (request.ActionId)
        {
            case QuickMenuFeature.SearchAction:
                CloseQuickMenu();
                OpenLauncher();
                return true;

            case QuickMenuFeature.TimerAction:
                CloseQuickMenu();

                if (request.Value == "0")
                {
                    _timerFeature.Reset();
                    _controller.RequestCollapse();
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

            case QuickMenuFeature.DetachAction:
                CloseQuickMenu();
                _controller.RequestCollapse();
                ScheduleDetachFromMenu();
                return true;

            case QuickMenuFeature.DockAction:
                CloseQuickMenu();
                _controller.RequestCollapse();
                DockFromMenu(Enum.TryParse(request.Value, out NotchEdge edge) ? edge : NotchEdge.Top);
                return true;

            case QuickMenuFeature.SettingsAction:
                CloseQuickMenu();
                _controller.RequestCollapse();
                OpenSettingsWindow();
                return true;

            case QuickMenuFeature.QuitAction:
                Application.Current.Exit();
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
        _quickMenuFeature.Dismiss();

        if (HasActivity(activityId))
        {
            _activityManager.PinPresentation(activityId);
            RevealPresented();
        }
        else
        {
            _activityManager.PinPresentation(null);
            _controller.RequestCollapse();
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
        _menuDetachTimer.Tick += (_, _) =>
        {
            _menuDetachTimer?.Stop();
            DetachFromMenu();
        };
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
