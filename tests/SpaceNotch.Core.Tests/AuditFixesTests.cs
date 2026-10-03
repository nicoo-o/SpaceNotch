using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Correctifs de l'audit professionnel (SN-07, SN-08, SN-19, SN-20).
/// </summary>
public sealed class AuditFixesTests
{
    private static readonly ScreenRect Work = new(0, 0, 1920, 1040);

    private static IslandActivity Activity(string id, string featureId = "feature.host", string title = "texte privé") => new()
    {
        Id = id,
        FeatureId = featureId,
        SceneKey = IslandSceneCatalog.Card,
        Title = title
    };

    // ------------------------------------------------------------------
    // SN-19 : un greffon ne touche qu'à ses propres activités
    // ------------------------------------------------------------------

    [Fact]
    public void Un_greffon_ne_voit_ni_ne_retire_les_activites_des_autres()
    {
        var host = new ActivityManager();
        host.PostActivity(Activity("media", "feature.media"));

        var plugin = new ScopedActivityManager(host);
        plugin.PostActivity(Activity("plugin.a", "plugin.feature"));

        Assert.Equal("plugin.a", Assert.Single(plugin.GetActiveActivities()).Id);
        Assert.Equal(1, plugin.Count);

        Assert.False(plugin.RemoveActivity("media"));
        Assert.Equal(0, plugin.RemoveActivitiesFrom("feature.media"));
        Assert.Equal(2, host.Count);

        Assert.Equal(1, plugin.RemoveActivitiesFrom("plugin.feature"));
        Assert.Equal("media", Assert.Single(host.GetActiveActivities()).Id);
    }

    [Fact]
    public void Un_greffon_ne_remplace_pas_une_activite_de_l_hote()
    {
        var host = new ActivityManager();
        host.PostActivity(Activity("media", "feature.media", "Good Days"));

        var plugin = new ScopedActivityManager(host);
        plugin.PostActivity(Activity("media", "plugin.feature", "piégé"));

        Assert.Equal("Good Days", host.GetActiveActivities().Single().Title);
        Assert.Empty(plugin.GetActiveActivities());
    }

    [Fact]
    public void Un_greffon_ne_pilote_ni_la_presentation_ni_l_expiration()
    {
        var host = new ActivityManager();
        host.PostActivity(Activity("a"));
        host.PostActivity(Activity("b"));

        var plugin = new ScopedActivityManager(host);
        IslandActivity? before = host.CurrentActivity;

        plugin.PinPresentation("a");
        Assert.False(plugin.CyclePresentation(1));
        Assert.Equal(0, plugin.ExpireOverdue(DateTimeOffset.MaxValue));

        Assert.Equal(before?.Id, host.CurrentActivity?.Id);
        Assert.Equal(2, host.Count);
        Assert.Null(plugin.CurrentActivity);
    }

    [Fact]
    public void Les_evenements_ne_montrent_au_greffon_que_ses_activites()
    {
        var host = new ActivityManager();
        var plugin = new ScopedActivityManager(host);
        IslandActivity? seen = Activity("sentinelle");
        int removed = 0;

        plugin.ActiveActivityChanged += (_, activity) => seen = activity;
        plugin.ActivityRemoved += (_, _) => removed++;

        host.PostActivity(Activity("notif"));
        Assert.Null(seen);

        host.RemoveActivity("notif");
        Assert.Equal(0, removed);

        plugin.PostActivity(Activity("plugin.a", "plugin.feature"));
        host.RemoveActivity("plugin.a");
        Assert.Equal(1, removed);
        Assert.Equal(0, plugin.Count);
    }

    // ------------------------------------------------------------------
    // SN-20 : un greffon lent ne retarde pas le démarrage
    // ------------------------------------------------------------------

    private sealed class BlockingFeature(IActivityManager activities, IEventBus events, ManualResetEventSlim gate)
        : IslandFeatureBase("plugin.slow", "Greffon lent", activities, events, isEnabled: true)
    {
        protected override Task OnStartAsync(CancellationToken cancellationToken)
        {
            // Bloque avant tout await, comme le greffon de sonde de l'audit.
            gate.Wait(TimeSpan.FromSeconds(10), cancellationToken);
            return Task.CompletedTask;
        }

        protected override Task OnStopAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task Un_greffon_qui_bloque_au_demarrage_ne_retient_pas_l_hote()
    {
        using var gate = new ManualResetEventSlim(false);
        var feature = new BlockingFeature(new ActivityManager(), new EventBus(), gate);
        var registry = new IslandFeatureRegistry([feature], startsOffThread: f => f == feature);

        Task start = registry.StartEnabledAsync();
        Task finished = await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(3)));

        Assert.Same(start, finished);

        gate.Set();

        for (int i = 0; i < 100 && feature.State != FeatureState.Running; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(FeatureState.Running, feature.State);
        await registry.DisposeAsync();
    }

    // ------------------------------------------------------------------
    // SN-08 : lâchée sur la languette, la notch s'y raccroche
    // ------------------------------------------------------------------

    [Fact]
    public void Lachee_sur_la_languette_avec_un_elan_oppose_elle_se_raccroche()
    {
        var pill = new ScreenRect(820, 30, 250, 52);

        FloatingTarget target = Detachment.Land(pill, 0, 2400, Work, Work.CenterX);

        Assert.Equal(FloatingLanding.Reattach, target.Landing);
        Assert.Equal(Work.Y, target.Y);
    }
}
