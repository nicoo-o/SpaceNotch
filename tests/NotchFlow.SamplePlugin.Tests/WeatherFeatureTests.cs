using System;
using System.Linq;
using System.Threading.Tasks;
using NotchFlow.Core.Activities;
using NotchFlow.Core.Events;
using NotchFlow.Core.Features;
using NotchFlow.Core.Scenes;
using NotchFlow.SamplePlugin.Weather;
using Xunit;

namespace NotchFlow.SamplePlugin.Tests;

/// <summary>
/// Comportement de la fonctionnalité météo.
///
/// Ces tests exercent le vrai cycle de vie — démarrage, publication, action,
/// arrêt — sur le vrai gestionnaire d'activités. Ce qui est simulé se limite à
/// l'entrée réseau, et cette frontière est exactement celle que le greffon a
/// choisie.
/// </summary>
public sealed class WeatherFeatureTests
{
    private static readonly WeatherLocation Paris = new("Paris", 48.8566, 2.3522);

    private static WeatherFeature CreateFeature(
        FakeWeatherSource source,
        out ActivityManager activities,
        out EventBus events)
    {
        activities = new ActivityManager();
        events = new EventBus();

        return new WeatherFeature(activities, events, source, Paris);
    }

    [Fact]
    public async Task Start_PublishesACardActivityWithoutBlockingOnTheReleve()
    {
        var source = new FakeWeatherSource();

        WeatherFeature feature = CreateFeature(source, out ActivityManager activities, out _);

        await feature.StartAsync();

        // Le démarrage a rendu la main avant que le relevé soit arrivé : il ne
        // dépend donc pas d'un accès réseau. La tâche est observable pour qui veut
        // savoir quand la carte est à jour.
        Assert.True(feature.State == FeatureState.Running);

        await feature.PendingRefresh;

        IslandActivity activity = Assert.Single(activities.GetActiveActivities());

        Assert.Equal(WeatherFeature.ActivityId, activity.Id);
        Assert.Equal(WeatherFeature.FeatureKey, activity.FeatureId);

        // La scène générique est celle qui accueille le contenu d'un greffon : c'est
        // par elle qu'il s'affiche sans que la fenêtre le connaisse.
        Assert.Equal(IslandSceneCatalog.Card, activity.SceneKey);

        // Information d'ambiance : elle ne doit jamais supplanter un appel ou une
        // notification.
        Assert.Equal(ActivityPriority.Background, activity.Priority);

        Assert.Contains("18 °C", activity.Title, StringComparison.Ordinal);
        Assert.Contains("Ciel dégagé", activity.Title, StringComparison.Ordinal);
        Assert.Contains("Paris", activity.Subtitle, StringComparison.Ordinal);

        // Le greffon apporte son propre glyphe : un unique caractère de la zone à
        // usage privé, sans dépendre d'une clé connue de l'hôte.
        Assert.Equal(1, activity.IconKey!.Length);

        Assert.Equal(2, activity.Actions.Count);
        Assert.Contains(activity.Actions, a => a.Id == WeatherFeature.RefreshAction && a.IsPrimary);
        Assert.Contains(activity.Actions, a => a.Id == WeatherFeature.ToggleUnitAction);

        await feature.DisposeAsync();
    }

    [Fact]
    public async Task Refresh_ReplacesThePreviousReleveInsteadOfAccumulating()
    {
        var source = new FakeWeatherSource()
            .Enqueue(new WeatherSnapshot(10, 0, true, DateTimeOffset.UtcNow))
            .Enqueue(new WeatherSnapshot(21, 3, true, DateTimeOffset.UtcNow));

        WeatherFeature feature = CreateFeature(source, out ActivityManager activities, out _);

        await feature.StartAsync();
        await feature.PendingRefresh;

        await feature.RefreshAsync();

        // Une seule activité : le gestionnaire remplace par identifiant. Sans cette
        // règle, quinze minutes de rafraîchissements empileraient des dizaines de
        // relevés périmés.
        IslandActivity activity = Assert.Single(activities.GetActiveActivities());

        Assert.Contains("21 °C", activity.Title, StringComparison.Ordinal);
        Assert.Contains("Couvert", activity.Title, StringComparison.Ordinal);

        await feature.DisposeAsync();
    }

    [Fact]
    public async Task RefreshAction_ReadsTheSourceAgainAndIsHandled()
    {
        var source = new FakeWeatherSource();

        WeatherFeature feature = CreateFeature(source, out _, out _);

        await feature.StartAsync();
        await feature.PendingRefresh;

        int callsBefore = source.Calls;

        bool handled = await feature.HandleActionAsync(
            new IslandActionRequest(WeatherFeature.ActivityId, WeatherFeature.RefreshAction));

        Assert.True(handled);
        Assert.Equal(callsBefore + 1, source.Calls);

        await feature.DisposeAsync();
    }

    [Fact]
    public async Task ToggleUnitAction_SwitchesDisplayWithoutReadingTheSourceAgain()
    {
        var source = new FakeWeatherSource();

        WeatherFeature feature = CreateFeature(source, out ActivityManager activities, out _);

        await feature.StartAsync();
        await feature.PendingRefresh;

        int callsBefore = source.Calls;

        bool handled = await feature.HandleActionAsync(
            new IslandActionRequest(WeatherFeature.ActivityId, WeatherFeature.ToggleUnitAction));

        Assert.True(handled);

        // 18 °C vaut 64,4 °F, arrondi à 64.
        IslandActivity activity = Assert.Single(activities.GetActiveActivities());
        Assert.Contains("°F", activity.Title, StringComparison.Ordinal);
        Assert.Contains("64", activity.Title, StringComparison.Ordinal);

        // L'unité est une décision d'affichage : aucune donnée n'a été relue.
        Assert.Equal(callsBefore, source.Calls);

        // L'étiquette du contrôle suivant décrit l'état atteignable, pas l'état courant.
        Assert.Contains(
            activity.Actions,
            a => a.Id == WeatherFeature.ToggleUnitAction
                && string.Equals(a.Label, "En °C", StringComparison.Ordinal));

        await feature.DisposeAsync();
    }

    [Fact]
    public async Task UnknownAction_IsDeclinedRatherThanSwallowed()
    {
        var source = new FakeWeatherSource();

        WeatherFeature feature = CreateFeature(source, out _, out _);

        await feature.StartAsync();
        await feature.PendingRefresh;

        Assert.False(await feature.HandleActionAsync(
            new IslandActionRequest(WeatherFeature.ActivityId, "weather.does-not-exist")));

        // Un identifiant d'activité qui n'est pas le sien est également refusé.
        Assert.False(await feature.HandleActionAsync(
            new IslandActionRequest("autre.activite", WeatherFeature.RefreshAction)));

        await feature.DisposeAsync();
    }

    [Fact]
    public async Task FailingSource_IsReportedWithoutFaultingTheFeature()
    {
        var source = new FakeWeatherSource
        {
            Failure = new InvalidOperationException("réseau indisponible")
        };

        WeatherFeature feature = CreateFeature(source, out ActivityManager activities, out _);

        Exception? reported = null;
        feature.ErrorReported += (_, exception) => reported = exception;

        await feature.StartAsync();
        await feature.PendingRefresh;

        // Une panne réseau ne met pas la fonctionnalité en échec : l'hôte n'a pas à
        // traiter l'indisponibilité d'un service tiers comme un incident.
        Assert.Equal(FeatureState.Running, feature.State);
        Assert.NotNull(reported);
        Assert.NotNull(feature.LastError);

        // Et rien de faux n'est affiché : pas de relevé, pas de carte.
        Assert.Empty(activities.GetActiveActivities());

        await feature.DisposeAsync();
    }

    [Fact]
    public async Task Stop_RemovesTheActivityAndSilencesFurtherRefresh()
    {
        var source = new FakeWeatherSource();

        WeatherFeature feature = CreateFeature(source, out ActivityManager activities, out _);

        await feature.StartAsync();
        await feature.PendingRefresh;

        Assert.Single(activities.GetActiveActivities());

        await feature.StopAsync();

        Assert.Empty(activities.GetActiveActivities());
        Assert.Equal(FeatureState.Stopped, feature.State);

        int callsBefore = source.Calls;

        await feature.RefreshAsync();

        // Arrêtée, la fonctionnalité ne travaille plus : ni appel, ni publication.
        // C'est la règle « inactive = zéro travail » appliquée à un greffon.
        Assert.Equal(callsBefore, source.Calls);
        Assert.Empty(activities.GetActiveActivities());

        await feature.DisposeAsync();
    }

    [Fact]
    public async Task Restart_IsReplayableWithoutLeakingTimers()
    {
        var source = new FakeWeatherSource();

        WeatherFeature feature = CreateFeature(source, out ActivityManager activities, out _);

        await feature.StartAsync();
        await feature.PendingRefresh;
        await feature.StopAsync();
        await feature.StartAsync();
        await feature.PendingRefresh;

        // Une réactivation produit une carte, et une seule : la symétrie entre
        // acquisition et libération est ce qui l'autorise.
        Assert.Single(activities.GetActiveActivities());

        await feature.DisposeAsync();
    }

    [Fact]
    public void Disabled_DoesNotStart()
    {
        var source = new FakeWeatherSource();

        var activities = new ActivityManager();
        var feature = new WeatherFeature(activities, new EventBus(), source, Paris, isEnabled: false);

        Assert.False(feature.IsEnabled);
        Assert.Empty(activities.GetActiveActivities());
    }
}
