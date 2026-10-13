using SpaceNotch.Core.Presentation;
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
    /// <summary>Le choix de barre déjà appliqué : les réglages arrivent modifiés sur place.</summary>
    private TopBarPlacement _appliedTopBar;

    /// <summary>La question a été posée pendant ce démarrage : elle ne revient pas.</summary>
    private bool _topBarAsked;

    /// <summary>
    /// Relit la barre des tâches de l'écran de la notch : demander si l'utilisateur
    /// n'a pas répondu, passer en languette si c'est son choix. La position sous
    /// la barre, elle, est appliquée par la géométrie.
    /// </summary>
    private void ApplyTopBar()
    {
        if (_isClosed || UsesFloatingGeometry)
        {
            return;
        }

        DisplayInfo display = ResolveDisplay();
        TopBarPlacement placement = _settings.TopBarPlacement;

        if (TopBar.WantsSide(display.Top, display.WorkTop, placement) && _edge == NotchEdge.Top)
        {
            MiniLogger.Log("[BARRE] Barre des tâches en haut : la notch passe en languette, à droite");
            _settingsService.Update(s =>
            {
                s.AllowSideEdges = true;
                s.DockEdge = NotchEdge.Right;
            });
            return;
        }

        if (TopBar.ShouldAsk(display.Top, display.WorkTop, placement)
            && _edge == NotchEdge.Top
            && !_topBarAsked
            && !_quickMenuFeature.IsTopBarOfferShown)
        {
            _topBarAsked = true;
            MiniLogger.Log($"[BARRE] Barre des tâches en haut ({TopBar.Reserved(display.Top, display.WorkTop)} px) : la notch demande où se placer");
            _quickMenuFeature.OfferTopBarPlacement();
            _activityManager.PinPresentation(QuickMenuFeature.TopBarOfferId);
            RevealPresented();
        }
    }
}
