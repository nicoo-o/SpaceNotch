using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.State;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Features.Menu;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Display;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Barre des tâches en haut (n° 93, ADR-030). La notch accrochée en haut se
/// pose sous la barre (voir ApplyGeometry) ; elle demande une fois par
/// démarrage où se placer, et passe en languette si c'est le choix.
/// </summary>
public sealed partial class IslandWindow
{
    /// <summary>La question a été posée pendant ce démarrage : elle ne revient pas.</summary>
    private bool _topBarAsked;

    /// <summary>Une question attend que la notch se referme (ou que la présentation finisse).</summary>
    private bool _topBarPending;

    /// <summary>Hauteur de la barre des tâches en haut de cet écran, en DIP ; 0 sans barre.</summary>
    private static double TopInsetDip(DisplayInfo display)
        => TopBar.TopInset(display.Top, display.WorkTop) / display.DpiScale;

    /// <summary>
    /// Une barre des tâches occupe le haut de l'écran de la notch, et
    /// l'utilisateur n'a pas répondu : la notch le lui demande, une fois par
    /// démarrage. Jamais par-dessus une scène ouverte ni la présentation : la
    /// question attend alors le repli (ou la fin de la présentation).
    /// </summary>
    private void ApplyTopBar()
    {
        if (_isClosed || UsesFloatingGeometry || _topBarAsked || _edge != NotchEdge.Top)
        {
            return;
        }

        DisplayInfo display = ResolveDisplay();

        if (!TopBar.ShouldAsk(display.Top, display.WorkTop, _settings.TopBarPlacement))
        {
            _topBarPending = false;
            return;
        }

        if (_controller.State != IslandState.Closed || _welcomeFeature.IsShown || !_settings.WelcomeCompleted)
        {
            _topBarPending = true;
            return;
        }

        _topBarPending = false;
        _topBarAsked = true;
        MiniLogger.Log($"[BARRE] Barre des tâches en haut ({TopBar.Reserved(display.Top, display.WorkTop)} px) : la notch demande où se placer");
        _quickMenuFeature.OfferTopBarPlacement();
        _activityManager.PinPresentation(QuickMenuFeature.TopBarOfferId);
        RevealPresented();
    }

    /// <summary>
    /// « Sur le côté » vient d'être choisi : la notch accrochée en haut passe en
    /// languette, à droite — une fois, au moment du choix (n° 93). Les côtés
    /// sont activés, puisque c'est le choix de l'utilisateur.
    /// </summary>
    private void MoveToSideForTopBar()
    {
        if (_isClosed || UsesFloatingGeometry || _edge != NotchEdge.Top)
        {
            return;
        }

        DisplayInfo display = ResolveDisplay();

        if (!TopBar.WantsSide(display.Top, display.WorkTop, _settings.TopBarPlacement))
        {
            return;
        }

        MiniLogger.Log("[BARRE] Barre des tâches en haut : la notch passe en languette, à droite");
        _settingsService.Update(s =>
        {
            s.AllowSideEdges = true;
            s.DockEdge = NotchEdge.Right;
        });
    }
}
