using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Display;

namespace SpaceNotch.Features.SystemHud;

/// <summary>
/// Retour visuel de luminosité.
///
/// Elle partage la scène du volume parce que la charge utile est la même : une
/// valeur, une échelle, un libellé et une clé d'icône. Une seconde scène aurait
/// dupliqué une mise en page identique pour la seule différence d'un glyphe.
/// </summary>
public sealed class BrightnessHudFeature : IslandFeatureBase
{
    public const string FeatureKey = "feature.brightness";

    private const string ActivityId = "feature.brightness.current";

    private static readonly TimeSpan HudLifetime = TimeSpan.FromSeconds(2);

    private readonly BrightnessService _brightness;

    public BrightnessHudFeature(
        IActivityManager activities,
        IEventBus events,
        BrightnessService brightness,
        bool isEnabled = true)
        : base(FeatureKey, "Luminosité", activities, events, isEnabled)
    {
        _brightness = brightness ?? throw new ArgumentNullException(nameof(brightness));
    }

    /// <summary>
    /// Vrai lorsque l'écran expose une luminosité réglable et que le crochet
    /// clavier est posé. Faux sur un poste à écran externe, où la fonctionnalité
    /// n'a rien à observer.
    /// </summary>
    public bool IsAvailable => _brightness.IsAvailable;

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _brightness.BrightnessChanged += OnBrightnessChanged;
        _brightness.Start();

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _brightness.BrightnessChanged -= OnBrightnessChanged;

        // Retire le crochet clavier : une fonctionnalité désactivée ne doit
        // intercepter aucune touche.
        _brightness.Stop();
        RemoveActivity(ActivityId);

        return Task.CompletedTask;
    }

    private void OnBrightnessChanged(object? sender, BrightnessInfo info)
    {
        var payload = new HudPayload(
            Value: info.Percent,
            Maximum: 100,
            Label: "Luminosité",
            ValueText: $"{info.Percent}%",
            IconKey: "Brightness");

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.BrightnessHud,
            Title = $"Luminosité {info.Percent}%",
            Subtitle = "Écran principal",
            Source = "System.Display",
            IconKey = "Brightness",
            State = IslandActivityState.SystemHud,
            Priority = ActivityPriority.High,
            Duration = HudLifetime,
            Payload = payload
        });
    }
}
