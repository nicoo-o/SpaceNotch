using System;
using System.Collections.Generic;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Machine;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.State;
using SpaceNotch.Infrastructure.Logging;

namespace SpaceNotch_App.Windows;

/// <summary>
/// La machine à états en ombre (phase E, RFC §3.6) : elle reçoit les mêmes
/// entrées que la notch — survol, appui, traction, lâcher, Échap, clic
/// ailleurs, raccourci, arrivées, présence, placement — et compare sa surface
/// à l'état réel chaque fois qu'il se pose. Elle ne pilote rien. Ses écarts,
/// journalisés « [OMBRE] », diront région par région quand elle peut prendre
/// la main ; la visite filmée, qui pose l'état directement, n'est pas comptée.
/// </summary>
public sealed partial class IslandWindow
{
    private readonly NotchShadow _shadow = new();
    private readonly HashSet<string> _shadowAnnounced = new(StringComparer.Ordinal);
    private string _shadowContext = "démarrage";

    /// <summary>Écarts journalisés au plus, pour ne jamais noyer le journal.</summary>
    private const int ShadowLogLimit = 40;

    /// <summary>Donne une entrée à l'ombre, sans jamais pouvoir gêner la notch.</summary>
    private void Shadow(NotchInput input, string context)
    {
        try
        {
            SyncShadowPlacement();
            _shadow.UseRules(new NotchRules(_settings.TearDistance, _settings.AllowDetach, _settings.HoverToPreview));
            _shadow.Feed(input);
            _shadowContext = context;
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[OMBRE] entrée refusée", ex);
        }
    }

    private void Shadow(NotchTrigger trigger, string context) => Shadow(new NotchInput(trigger), context);

    /// <summary>Le placement réel, donné à l'ombre s'il a changé.</summary>
    private void SyncShadowPlacement()
    {
        Placement placement = UsesFloatingGeometry ? Placement.Floating : UsesSideTab ? Placement.Tab : Placement.Attached;

        if (_shadow.Current.Placement != placement && _shadow.Current.Hand == HandPhase.Free)
        {
            _shadow.Feed(new NotchInput(NotchTrigger.PlacementChanged, Placement: placement));
        }
    }

    /// <summary>L'état réel s'est posé : l'ombre est comparée, puis réalignée.</summary>
    private void CompareShadow(IslandState state)
    {
        if (state is IslandState.Expanding or IslandState.Collapsing)
        {
            return;
        }

        try
        {
            string? divergence = _shadow.Compare(state, _touring ? "visite" : _shadowContext);

            if (divergence is not null)
            {
                if (!_touring && _shadow.Divergences <= ShadowLogLimit)
                {
                    MiniLogger.Log(divergence);
                }

                _shadow.Resync(state);
            }
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[OMBRE] comparaison impossible", ex);
        }
    }

    /// <summary>Arrivée ou départ d'une activité, vu par l'ombre.</summary>
    private void ShadowPresented(IslandActivity? activity)
    {
        if (activity is null)
        {
            Shadow(NotchTrigger.ActivitiesEmptied, "plus d'activité");
            return;
        }

        // Comme le contrôleur : une activité importante ne s'annonce qu'une fois.
        bool claims = activity.Priority >= ActivityPriority.High && _shadowAnnounced.Add(activity.Id);
        Shadow(new NotchInput(NotchTrigger.ActivityArrived, HasPresented: true, ClaimsAttention: claims), "arrivée " + activity.Id);
    }

    private void ShadowForget(IslandActivity activity) => _shadowAnnounced.Remove(activity.Id);

    /// <summary>Le lâcher, avec tout ce que la main a fait.</summary>
    private void ShadowRelease(double pull, double lateral, double velocity, double held)
        => Shadow(
            new NotchInput(
                NotchTrigger.Release,
                Device: _pressByTouch ? PointerKind.Touch : PointerKind.Mouse,
                PullDip: pull,
                LateralDip: lateral,
                VelocityDip: velocity,
                HeldSeconds: held,
                HasPresented: _controller.PresentedActivity is not null),
            "lâcher");
}
