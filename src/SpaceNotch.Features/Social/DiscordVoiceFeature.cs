using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.Social;
using SpaceNotch.Core.State;

namespace SpaceNotch.Features.Social;

/// <summary>
/// La salle vocale Discord dans la notch (T3) : qui est là (une identicône
/// chacun, allumée quand il parle) et le micro, coupé ou non, d'un clic.
/// La connexion au client Discord est faite par la plateforme ; la
/// fonctionnalité reçoit l'état de la salle et renvoie la bascule du micro.
/// </summary>
public sealed class DiscordVoiceFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Discord;

    public const string ActivityId = "feature.discord.voice";

    public const string MuteAction = "discord.mute";

    public static readonly ActivityTint Blurple = new(0x58, 0x65, 0xF2);

    private bool _selfMuted;

    public DiscordVoiceFeature(IActivityManager activities, IEventBus events, bool isEnabled)
        : base(FeatureKey, "Discord (vocal)", activities, events, isEnabled)
    {
    }

    /// <summary>Coupe ou rouvre son micro dans Discord.</summary>
    public Func<bool, Task<bool>>? SetMute { get; set; }

    /// <summary>
    /// L'état de la salle. Hors d'une salle (<paramref name="channelId"/> nul),
    /// la carte disparaît. Appelable directement (tests, visite).
    /// </summary>
    public void Show(string? channelId, string? channelName, IReadOnlyList<VoiceMember> members, bool selfMuted)
    {
        ArgumentNullException.ThrowIfNull(members);
        _selfMuted = selfMuted;

        if (channelId is null || !IsEnabled)
        {
            RemoveActivity(ActivityId);
            return;
        }

        var speaking = members.Where(m => m.Speaking).Select(m => m.Name).ToList();
        string subtitle = speaking.Count switch
        {
            0 => members.Count == 1 ? Lang.T("1 personne", "1 person") : members.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + Lang.T(" personnes", " people"),
            1 => speaking[0] + Lang.T(" parle", " is speaking"),
            _ => string.Join(", ", speaking.Take(2)) + Lang.T(" parlent", " are speaking")
        };

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Eyebrow = Lang.T("Discord · vocal", "Discord · voice"),
            Title = string.IsNullOrWhiteSpace(channelName) ? Lang.T("Salon vocal", "Voice channel") : channelName,
            Subtitle = subtitle,
            Source = "Discord",
            IconKey = "Headphones",
            Metric = selfMuted ? Lang.T("muet", "muted") : null,
            Tint = Blurple,
            State = IslandActivityState.MediaActive,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            Payload = new VoicePayload(channelName, members, selfMuted),

            // La rangée d'identicônes au-dessus du bouton du micro.
            ExpandedFootprint = IslandSceneCatalog.FootprintFor(IslandSceneCatalog.Card) is var card ? new IslandFootprint(card.Width, card.Height + 40) : null,
            Actions =
            [
                new ActivityAction(
                    MuteAction,
                    selfMuted ? Lang.T("Réactiver le micro", "Unmute") : Lang.T("Couper le micro", "Mute"),
                    selfMuted ? "MicrophoneOff" : "Microphone",
                    ActivityActionKind.Toggle,
                    IsPrimary: true,
                    IsEnabled: SetMute is not null)
            ]
        });
    }

    public override async Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ActivityId != ActivityId || request.ActionId != MuteAction || SetMute is not { } setMute)
        {
            return false;
        }

        await setMute(!_selfMuted).ConfigureAwait(false);
        return true;
    }

    protected override Task OnStartAsync(System.Threading.CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task OnStopAsync()
    {
        RemoveActivity(ActivityId);
        return Task.CompletedTask;
    }
}
