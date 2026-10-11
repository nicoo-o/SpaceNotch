using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Menu;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;

namespace SpaceNotch.Features.Menu;

/// <summary>
/// Menu rapide : clic droit sur la notch, qui s'ouvre en menu — même noir,
/// mêmes épaules. La fonctionnalité ne fait que présenter l'activité ; les
/// commandes (détacher, accrocher, quitter…) touchent la fenêtre et sont
/// exécutées par elle. Seul le dépliage d'« Accrocher à… », qui change la
/// hauteur, passe par ici.
/// </summary>
public sealed class QuickMenuFeature : IslandFeatureBase
{
    public const string FeatureKey = "feature.quickmenu";

    public const string SearchAction = "menu.search";
    public const string TimerAction = "menu.timer";
    public const string ClipboardAction = "menu.clipboard";
    public const string ShelfAction = "menu.shelf";
    public const string NoteAction = "menu.note";
    public const string DetachAction = "menu.detach";
    public const string DockAction = "menu.dock";
    public const string DockExpandAction = "menu.dock.expand";
    public const string SettingsAction = "menu.settings";
    public const string QuitAction = "menu.quit";

    /// <summary>Tuile « Plus » du tableau de bord (ADR-028) : ouvre ce menu.</summary>
    public const string MoreAction = "menu.more";

    public const string ActivityId = "feature.quickmenu.current";

    /// <summary>La carte qui propose d'activer l'historique du presse-papier (n° 63).</summary>
    public const string ClipboardOfferId = "feature.quickmenu.clipboard-offer";

    /// <summary>« Activer » sur cette carte.</summary>
    public const string ClipboardEnableAction = "menu.clipboard.enable";

    /// <summary>« Plus tard » sur cette carte.</summary>
    public const string ClipboardLaterAction = "menu.clipboard.later";

    private QuickMenuPayload? _state;

    public QuickMenuFeature(IActivityManager activities, IEventBus events)
        : base(FeatureKey, "Menu rapide", activities, events, isEnabled: true)
    {
    }

    /// <summary>Vrai tant que le menu est présenté.</summary>
    public bool IsShown => _state is not null;

    /// <summary>Présente le menu, décrit par la fenêtre qui seule connaît son bord et son état.</summary>
    public void Show(QuickMenuPayload state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state with { DockExpanded = false };
        Publish();
    }

    public void Dismiss()
    {
        if (_state is null)
        {
            return;
        }

        _state = null;
        RemoveActivity(ActivityId);
    }

    /// <summary>
    /// Historique éteint (le défaut, par discrétion) : la tuile Presse-papier
    /// n'est plus grisée sans explication. Elle propose de l'activer, en disant
    /// ce qu'il garde (n° 63).
    /// </summary>
    public void OfferClipboardHistory()
    {
        string subtitle = Lang.T(
            "Garde tes dernières copies pour les retrouver. En mémoire seulement, sans les mots de passe.",
            "Keeps your latest copies so you can find them again. In memory only, never passwords.");

        PublishActivity(new IslandActivity
        {
            Id = ClipboardOfferId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T("Activer l'historique du presse-papier ?", "Turn on clipboard history?"),
            Subtitle = subtitle,
            Source = "SpaceNotch",
            IconKey = "Clipboard",
            Layout = ActivityLayout.Stack,
            State = IslandActivityState.Notification,
            Priority = ActivityPriority.High,
            ExpandedFootprint = CardFit.For(eyebrow: false, subtitle, progress: false, actions: true, ActivityLayout.Stack),
            Actions =
            [
                new ActivityAction(ClipboardEnableAction, Lang.T("Activer", "Turn on"), "Check", ActivityActionKind.Invoke, IsPrimary: true, Tone: ActivityActionTone.Positive),
                new ActivityAction(ClipboardLaterAction, Lang.T("Plus tard", "Not now"), "Close")
            ]
        });
    }

    /// <summary>Retire la proposition, acceptée ou non.</summary>
    public void WithdrawClipboardOffer() => RemoveActivity(ClipboardOfferId);

    protected override Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task OnStopAsync()
    {
        Dismiss();
        return Task.CompletedTask;
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ActionId == DockExpandAction && _state is not null)
        {
            _state = _state with { DockExpanded = request.Value == "1" };
            Publish();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private void Publish()
    {
        if (_state is null)
        {
            return;
        }

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.QuickMenu,
            Title = "SpaceNotch",
            Source = "SpaceNotch",
            IconKey = "Menu",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            ExpandedFootprint = new IslandFootprint(QuickMenuLayout.Width, QuickMenuLayout.HeightFor(_state.DockExpanded)),
            MotionState = ActivityMotionState.Idle,
            Payload = _state
        });
    }
}
