using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
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
    public const string DetachAction = "menu.detach";
    public const string DockAction = "menu.dock";
    public const string DockExpandAction = "menu.dock.expand";
    public const string SettingsAction = "menu.settings";
    public const string QuitAction = "menu.quit";

    public const string ActivityId = "feature.quickmenu.current";

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
