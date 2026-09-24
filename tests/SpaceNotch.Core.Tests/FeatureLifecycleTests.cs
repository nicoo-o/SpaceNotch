using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Vérifie la promesse centrale du cahier des charges : « fonctionnalité
/// inactive = zéro travail ».
///
/// Ces tests ne mesurent pas la consommation ; ils vérifient la propriété qui la
/// rend vraie — l'acquisition des écouteurs système est strictement symétrique de
/// leur libération, et l'arrêt d'une fonctionnalité retire aussi les activités
/// qu'elle avait publiées. Sans cela, une bascule d'activation ne serait qu'un
/// masquage derrière lequel les abonnements continueraient de s'accumuler.
/// </summary>
public class FeatureLifecycleTests
{
    private const string FakeFeatureId = "feature.fake";

    /// <summary>
    /// Fonctionnalité de test qui compte ses acquisitions et libérations
    /// d'écouteurs, à la manière d'un vrai abonnement système.
    /// </summary>
    private sealed class FakeFeature : IslandFeatureBase
    {
        public FakeFeature(
            IActivityManager activities,
            IEventBus events,
            string id = FakeFeatureId,
            bool isEnabled = true,
            bool failOnStart = false)
            : base(id, "Fausse fonctionnalité", activities, events, isEnabled)
        {
            FailOnStart = failOnStart;
        }

        public bool FailOnStart { get; set; }

        public bool HandlesMessages { get; set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        /// <summary>
        /// Nombre d'écouteurs système actuellement acquis. Il doit revenir à zéro
        /// après un arrêt : c'est la mesure directe de « zéro travail ».
        /// </summary>
        public int LiveListeners { get; private set; }

        public void Raise(
            string activityId = "fake.activity",
            string sceneKey = IslandSceneCatalog.Pill,
            string ownerFeatureId = FakeFeatureId)
        {
            PublishActivity(new IslandActivity
            {
                Id = activityId,
                FeatureId = ownerFeatureId,
                SceneKey = sceneKey,
                Title = "activité"
            });
        }

        protected override Task OnStartAsync(CancellationToken cancellationToken)
        {
            if (FailOnStart)
            {
                throw new InvalidOperationException("API système indisponible");
            }

            StartCount++;
            LiveListeners++;

            return Task.CompletedTask;
        }

        protected override Task OnStopAsync()
        {
            StopCount++;
            LiveListeners--;

            return Task.CompletedTask;
        }

        public override bool TryHandleWindowMessage(uint messageId, nuint wParam) => HandlesMessages;
    }

    private static (FakeFeature Feature, ActivityManager Activities, EventBus Events) Create(
        string id = FakeFeatureId,
        bool isEnabled = true,
        bool failOnStart = false)
    {
        var activities = new ActivityManager();
        var events = new EventBus();
        var feature = new FakeFeature(activities, events, id, isEnabled, failOnStart);

        return (feature, activities, events);
    }

    // ------------------------------------------------------------------
    // Cycle de vie de base
    // ------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_IsIdempotent_AndAcquiresListenerOnce()
    {
        (FakeFeature feature, _, _) = Create();

        await feature.StartAsync();
        await feature.StartAsync();

        // Démarrer deux fois ne doit pas produire deux abonnements : sinon
        // l'Island recevrait chaque événement système en double.
        Assert.Equal(1, feature.StartCount);
        Assert.Equal(1, feature.LiveListeners);
        Assert.Equal(FeatureState.Running, feature.State);
    }

    [Fact]
    public async Task Disabling_ReleasesListenersAndRemovesPublishedActivities()
    {
        (FakeFeature feature, ActivityManager activities, _) = Create();

        await feature.StartAsync();
        feature.Raise();

        Assert.Equal(1, activities.Count);

        await feature.SetEnabledAsync(false);

        // Les deux moitiés de la promesse : plus aucun écouteur, plus aucune
        // activité laissée derrière.
        Assert.Equal(0, feature.LiveListeners);
        Assert.Equal(FeatureState.Stopped, feature.State);
        Assert.False(feature.IsEnabled);
        Assert.Equal(0, activities.Count);
        Assert.Null(activities.CurrentActivity);
    }

    [Fact]
    public async Task Reenabling_ReacquiresListenerWithoutDuplicating()
    {
        (FakeFeature feature, _, _) = Create();

        await feature.StartAsync();
        await feature.SetEnabledAsync(false);
        await feature.SetEnabledAsync(true);

        Assert.Equal(2, feature.StartCount);
        Assert.Equal(1, feature.StopCount);

        // Un seul écouteur vivant après un aller-retour : la bascule est
        // rejouable autant de fois que l'utilisateur le souhaite.
        Assert.Equal(1, feature.LiveListeners);
        Assert.Equal(FeatureState.Running, feature.State);
    }

    [Fact]
    public async Task SetEnabledAsync_IsIdempotent()
    {
        (FakeFeature feature, _, _) = Create();

        await feature.SetEnabledAsync(false);
        await feature.SetEnabledAsync(false);

        Assert.Equal(0, feature.StartCount);
        Assert.Equal(0, feature.StopCount);
    }

    [Fact]
    public async Task FeatureDisabledAtStart_DoesNotAcquireAnything()
    {
        (FakeFeature feature, _, _) = Create(isEnabled: false);

        await feature.StartAsync();

        Assert.Equal(0, feature.StartCount);
        Assert.Equal(0, feature.LiveListeners);
        Assert.Equal(FeatureState.Stopped, feature.State);
    }

    [Fact]
    public async Task StartFailure_IsIsolatedAndReported()
    {
        (FakeFeature feature, _, _) = Create(failOnStart: true);

        // Un échec d'API système ne doit jamais remonter : l'Island doit
        // s'afficher même si une fonctionnalité est indisponible.
        await feature.StartAsync();

        Assert.Equal(FeatureState.Faulted, feature.State);
        Assert.NotNull(feature.LastError);
        Assert.Equal(0, feature.LiveListeners);
    }

    [Fact]
    public async Task StopAsync_OnNeverStartedFeature_DoesNothing()
    {
        (FakeFeature feature, _, _) = Create();

        await feature.StopAsync();

        Assert.Equal(0, feature.StopCount);
        Assert.Equal(FeatureState.Stopped, feature.State);
    }

    [Fact]
    public async Task DisposeAsync_StopsAndReleasesEverything_Once()
    {
        (FakeFeature feature, ActivityManager activities, _) = Create();

        await feature.StartAsync();
        feature.Raise();

        await feature.DisposeAsync();
        await feature.DisposeAsync();

        Assert.Equal(0, feature.LiveListeners);
        Assert.Equal(0, activities.Count);
    }

    [Fact]
    public async Task PublishingActivity_AlsoSignalsItOnTheBus()
    {
        (FakeFeature feature, _, EventBus events) = Create();

        ActivityStartedEvent? observed = null;
        events.Subscribe<ActivityStartedEvent>(e => observed = e);

        await feature.StartAsync();
        feature.Raise(activityId: "fake.activity", sceneKey: IslandSceneCatalog.VolumeHud);

        Assert.NotNull(observed);
        Assert.Equal("fake.activity", observed!.ActivityId);
        Assert.Equal(FakeFeatureId, observed.FeatureId);
        Assert.Equal(IslandSceneCatalog.VolumeHud, observed.SceneKey);
    }

    [Fact]
    public async Task GetActivities_ReturnsOnlyOwnActivities()
    {
        (FakeFeature feature, _, _) = Create();

        await feature.StartAsync();
        feature.Raise(activityId: "mine");
        feature.Raise(activityId: "someone.else", ownerFeatureId: "feature.other");

        IslandActivity only = Assert.Single(feature.GetActivities());
        Assert.Equal("mine", only.Id);
    }

    // ------------------------------------------------------------------
    // Registre
    // ------------------------------------------------------------------

    [Fact]
    public async Task Registry_StartsOnlyEnabledFeatures()
    {
        var activities = new ActivityManager();
        var events = new EventBus();

        var enabled = new FakeFeature(activities, events, "feature.on");
        var disabled = new FakeFeature(activities, events, "feature.off", isEnabled: false);

        await using var registry = new IslandFeatureRegistry([enabled, disabled]);
        await registry.StartEnabledAsync();

        Assert.Equal(1, enabled.LiveListeners);
        Assert.Equal(0, disabled.LiveListeners);
        Assert.Equal(1, registry.RunningCount);
    }

    [Fact]
    public async Task Registry_OneFaultedFeatureDoesNotPreventTheOthers()
    {
        var activities = new ActivityManager();
        var events = new EventBus();

        var faulty = new FakeFeature(activities, events, "feature.faulty", failOnStart: true);
        var healthy = new FakeFeature(activities, events, "feature.healthy");

        await using var registry = new IslandFeatureRegistry([faulty, healthy]);
        await registry.StartEnabledAsync();

        Assert.Equal(FeatureState.Faulted, faulty.State);
        Assert.Equal(FeatureState.Running, healthy.State);
        Assert.Equal(1, healthy.LiveListeners);
    }

    [Fact]
    public async Task Registry_TogglesFeatureById()
    {
        var activities = new ActivityManager();
        var events = new EventBus();
        var feature = new FakeFeature(activities, events);

        await using var registry = new IslandFeatureRegistry([feature]);
        await registry.StartEnabledAsync();

        Assert.True(await registry.SetEnabledAsync(FakeFeatureId, false));
        Assert.Equal(0, feature.LiveListeners);

        Assert.True(await registry.SetEnabledAsync(FakeFeatureId, true));
        Assert.Equal(1, feature.LiveListeners);

        // Un identifiant inconnu est signalé, sans lever.
        Assert.False(await registry.SetEnabledAsync("feature.inexistante", true));
    }

    [Fact]
    public async Task Registry_StartEnabledAsync_DoesNotResurrectDisabledFeature()
    {
        var activities = new ActivityManager();
        var events = new EventBus();
        var feature = new FakeFeature(activities, events);

        await using var registry = new IslandFeatureRegistry([feature]);

        await registry.SetEnabledAsync(FakeFeatureId, false);
        await registry.StartEnabledAsync();

        Assert.Equal(0, feature.LiveListeners);
    }

    [Fact]
    public async Task Registry_RoutesWindowMessagesOnlyToRunningFeatures()
    {
        var activities = new ActivityManager();
        var events = new EventBus();
        var feature = new FakeFeature(activities, events) { HandlesMessages = true };

        await using var registry = new IslandFeatureRegistry([feature]);

        // Arrêtée : elle ne consomme rien, même si elle saurait traiter le message.
        Assert.False(registry.TryHandleWindowMessage(0x031D, 0));

        await registry.StartEnabledAsync();
        Assert.True(registry.TryHandleWindowMessage(0x031D, 0));

        await registry.SetEnabledAsync(FakeFeatureId, false);

        // C'est le point qui compte : après libération des écouteurs, plus aucun
        // message ne lui est acheminé.
        Assert.False(registry.TryHandleWindowMessage(0x031D, 0));
    }

    [Fact]
    public async Task Registry_DisposeStopsEverything()
    {
        var activities = new ActivityManager();
        var events = new EventBus();
        var first = new FakeFeature(activities, events, "feature.one");
        var second = new FakeFeature(activities, events, "feature.two");

        var registry = new IslandFeatureRegistry([first, second]);
        await registry.StartEnabledAsync();

        first.Raise(activityId: "activity.one", ownerFeatureId: "feature.one");
        second.Raise(activityId: "activity.two", ownerFeatureId: "feature.two");

        Assert.Equal(2, activities.Count);

        await registry.DisposeAsync();

        Assert.Equal(0, first.LiveListeners);
        Assert.Equal(0, second.LiveListeners);
        Assert.Equal(0, activities.Count);
    }

    [Fact]
    public async Task Registry_ReportsFaultsThroughTheCallback()
    {
        var activities = new ActivityManager();
        var events = new EventBus();
        var feature = new FakeFeature(activities, events, failOnStart: true);

        var reported = new System.Collections.Generic.List<string>();

        await using var registry = new IslandFeatureRegistry(
            [feature],
            onFault: (id, _) => reported.Add(id));

        await registry.StartEnabledAsync();

        Assert.Contains(FakeFeatureId, reported);
    }
}
