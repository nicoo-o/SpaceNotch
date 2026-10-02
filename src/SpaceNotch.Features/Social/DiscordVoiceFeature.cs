using System;
using System.Collections.Generic;
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

    /// <summary>Le lilas de la maquette pour le casque du salon.</summary>
    public static readonly ActivityTint Lilac = new(0xB9, 0xA8, 0xFF);

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

        string name = string.IsNullOrWhiteSpace(channelName) ? Lang.T("Salon vocal", "Voice channel") : channelName;

        // Maquette T3 : une seule ligne — le casque, le nom du salon, les avatars
        // (celui qui parle s'éclaire), puis le micro, rouge quand il est coupé.
        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = selfMuted ? name + Lang.T(" · Micro coupé", " · Mic muted") : name,
            Source = "Discord",
            IconKey = "Headphones",
            Tint = Lilac,
            State = IslandActivityState.MediaActive,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            Payload = new VoicePayload(channelName, members, selfMuted),
            Layout = ActivityLayout.Row,
            ExpandedFootprint = SceneInsets.Wrap(320, 30),
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
