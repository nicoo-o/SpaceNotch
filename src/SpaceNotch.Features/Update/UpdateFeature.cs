using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Core.Update;

namespace SpaceNotch.Features.Update;

/// <summary>
/// La mise à jour, vue dans la notch : une carte « SpaceNotch 1.17.0 est
/// prête » avec Installer et Plus tard, une carte qui dit l'installation en
/// cours, et, au redémarrage, « Mise à jour faite ». Le réseau et
/// l'installeur sont pilotés par l'application ; cette fonctionnalité ne fait
/// que montrer et transmettre les choix.
/// </summary>
public sealed class UpdateFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Update;

    public const string ActivityId = "feature.update.card";

    public const string InstallAction = "update.install";

    public const string LaterAction = "update.later";

    public const string NotesAction = "update.notes";

    /// <summary>Bleu calme : une information, pas une alerte.</summary>
    public static readonly ActivityTint Blue = new(0x7F, 0xB8, 0xFF);

    public UpdateFeature(IActivityManager activities, IEventBus events, bool isEnabled = true)
        : base(FeatureKey, Lang.T("Mises à jour", "Updates"), activities, events, isEnabled)
    {
    }

    /// <summary>« Installer » choisi.</summary>
    public event Action? InstallRequested;

    /// <summary>« Plus tard » choisi.</summary>
    public event Action? LaterRequested;

    /// <summary>« Nouveautés » choisi : la page de la release.</summary>
    public event Action? NotesRequested;

    protected override Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task OnStopAsync()
    {
        RemoveActivity(ActivityId);
        return Task.CompletedTask;
    }

    /// <summary>Durée d'affichage de la proposition : ensuite, elle se retire d'elle-même.</summary>
    public static readonly TimeSpan OfferLifetime = TimeSpan.FromSeconds(20);

    /// <summary>Une version plus récente est prête (ou, en portable, disponible).</summary>
    public void ShowReady(Version version, bool canInstall)
    {
        ArgumentNullException.ThrowIfNull(version);

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T($"SpaceNotch {UpdateRules.Display(version)}", $"SpaceNotch {UpdateRules.Display(version)}"),
            Subtitle = canInstall
                ? Lang.T("Mise à jour prête · la notch revient d'elle-même", "Update ready · the notch comes right back")
                : Lang.T("Nouvelle version disponible", "New version available"),
            Source = Lang.T("Mise à jour", "Update"),
            IconKey = "Download",
            Tint = Blue,
            State = IslandActivityState.Idle,
            MotionState = ActivityMotionState.Idle,
            Priority = ActivityPriority.Normal,

            // Proposée, puis effacée : elle ne doit pas occuper la notch tant
            // que personne n'y répond (audit SN-05).
            Duration = OfferLifetime,
            Actions = canInstall
                ?
                [
                    new ActivityAction(InstallAction, Lang.T("Installer", "Install"), "Download", ActivityActionKind.Invoke, IsPrimary: true, Tone: ActivityActionTone.Positive),
                    new ActivityAction(LaterAction, Lang.T("Plus tard", "Later"), "Close")
                ]
                :
                [
                    new ActivityAction(NotesAction, Lang.T("Télécharger", "Download"), "Download", ActivityActionKind.Invoke, IsPrimary: true),
                    new ActivityAction(LaterAction, Lang.T("Plus tard", "Later"), "Close")
                ]
        });
    }

    /// <summary>L'installation commence : la notch va se fermer un instant.</summary>
    public void ShowInstalling(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T($"Mise à jour vers {UpdateRules.Display(version)}", $"Updating to {UpdateRules.Display(version)}"),
            Subtitle = Lang.T("La notch revient dans un instant", "The notch will be right back"),
            Source = Lang.T("Mise à jour", "Update"),
            IconKey = "Download",
            Tint = Blue,
            State = IslandActivityState.DownloadActive,
            MotionState = ActivityMotionState.Working,
            Priority = ActivityPriority.Normal
        });
    }

    /// <summary>Au redémarrage, la mise à jour est faite.</summary>
    public void ShowUpdated(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T($"À jour : {UpdateRules.Display(version)}", $"Up to date: {UpdateRules.Display(version)}"),
            Subtitle = Lang.T("Mise à jour faite", "Update installed"),
            Source = Lang.T("Mise à jour", "Update"),
            IconKey = "Check",
            Tint = Blue,
            State = IslandActivityState.Idle,
            MotionState = ActivityMotionState.Complete,
            Priority = ActivityPriority.Normal,
            Duration = TimeSpan.FromSeconds(8),
            Actions = [new ActivityAction(NotesAction, Lang.T("Nouveautés", "What's new"), "Info")]
        });
    }

    /// <summary>Retire la carte.</summary>
    public void Clear() => RemoveActivity(ActivityId);

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.ActivityId, ActivityId, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        switch (request.ActionId)
        {
            case InstallAction:
                InstallRequested?.Invoke();
                return Task.FromResult(true);

            case LaterAction:
                RemoveActivity(ActivityId);
                LaterRequested?.Invoke();
                return Task.FromResult(true);

            case NotesAction:
                NotesRequested?.Invoke();
                return Task.FromResult(true);

            default:
                return Task.FromResult(false);
        }
    }
}
