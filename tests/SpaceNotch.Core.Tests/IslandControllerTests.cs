using System;
using System.Collections.Generic;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Animation;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Le contrôleur de l'Island, enfin testé (phase E) : il vit en Core derrière
/// un animateur que les tests remplacent par un faux qui se pose à la demande.
/// </summary>
public sealed class IslandControllerTests
{
    private static readonly IslandFootprint Rest = new(120, 28);

    private sealed class FakeAnimator(Action<IslandFootprint> update, Action settled) : IShapeAnimator
    {
        public IslandFootprint Current { get; private set; } = Rest;

        public IslandFootprint Target { get; private set; } = Rest;

        public double HeightVelocity { get; private set; }

        public bool IsRunning { get; private set; }

        public SpringParameters? Parameters { get; private set; }

        public List<IslandFootprint> Targets { get; } = [];

        public long RenderedFrames => 0;

        public double MaxFrameGapMilliseconds => 0;

        public long LongFrameCount => 0;

        public double MeasuredFrameRate => 0;

        public void UpdateParameters(SpringParameters parameters) => Parameters = parameters;

        public void AnimateTo(IslandFootprint target)
        {
            Target = target;
            Targets.Add(target);
            IsRunning = true;
        }

        public void SnapTo(IslandFootprint footprint)
        {
            IsRunning = false;
            Current = Target = footprint;
            HeightVelocity = 0;
            update(footprint);
        }

        public void Seed(IslandFootprint from, double heightVelocity)
        {
            Current = from;
            HeightVelocity = heightVelocity;
        }

        public void Stop() => IsRunning = false;

        /// <summary>Le ressort arrive : la forme est posée, l'hôte est prévenu.</summary>
        public void Settle()
        {
            IsRunning = false;
            Current = Target;
            HeightVelocity = 0;
            update(Current);
            settled();
        }
    }

    private static (IslandController Controller, FakeAnimator Animator, ActivityManager Activities) Create(bool springs = true)
    {
        var activities = new ActivityManager();
        FakeAnimator? animator = null;
        var controller = new IslandController(
            new IslandStateManager(),
            activities,
            MotionPresets.NaturalOpen,
            SpringParameters.FromResponse(0.30, 0.61),
            Rest,
            () => springs,
            _ => { },
            (update, settled) => animator = new FakeAnimator(update, settled));

        controller.PreviewFootprint = () => new IslandFootprint(140, 34);
        return (controller, animator!, activities);
    }

    private static IslandActivity Music(ActivityPriority priority = ActivityPriority.Normal, string title = "Good Days") => new()
    {
        Id = "music",
        FeatureId = "feature.media",
        SceneKey = IslandSceneCatalog.Media,
        Title = title,
        Priority = priority
    };

    [Fact]
    public void Le_survol_annonce_l_apercu_puis_revient_au_repos()
    {
        (IslandController controller, FakeAnimator animator, _) = Create();

        controller.RequestPreview();
        Assert.Equal(IslandState.Preview, controller.State);
        Assert.Equal(new IslandFootprint(140, 34), animator.Target);

        controller.EndPreview();
        Assert.Equal(IslandState.Closed, controller.State);
        Assert.Equal(Rest, animator.Target);
    }

    [Fact]
    public void Replier_depuis_l_apercu_ferme_sans_passer_par_Collapsing()
    {
        (IslandController controller, _, _) = Create();

        controller.RequestPreview();
        controller.RequestCollapse();

        Assert.Equal(IslandState.Closed, controller.State);
    }

    [Fact]
    public void Ouvrir_puis_se_poser_donne_Expanded_et_fermer_donne_Closed()
    {
        (IslandController controller, FakeAnimator animator, ActivityManager activities) = Create();
        activities.PostActivity(Music());

        controller.RequestExpand();
        Assert.Equal(IslandState.Expanding, controller.State);
        Assert.Equal(MotionPresets.NaturalOpen, animator.Parameters);

        animator.Settle();
        Assert.Equal(IslandState.Expanded, controller.State);

        controller.RequestCollapse();
        Assert.Equal(IslandState.Collapsing, controller.State);
        Assert.Equal(MotionPresets.CloseOf(MotionPresets.NaturalOpen), animator.Parameters);

        animator.Settle();
        Assert.Equal(IslandState.Closed, controller.State);
        Assert.Equal(Rest, animator.Current);
    }

    [Fact]
    public void Sans_rien_a_presenter_ouvrir_ne_fait_rien()
    {
        (IslandController controller, _, _) = Create();

        controller.RequestExpand();

        Assert.Equal(IslandState.Closed, controller.State);
    }

    [Fact]
    public void Une_activite_importante_s_ouvre_une_seule_fois()
    {
        (IslandController controller, FakeAnimator animator, ActivityManager activities) = Create();

        activities.PostActivity(Music(ActivityPriority.High));
        Assert.Equal(IslandState.Expanding, controller.State);
        Assert.Equal(MotionPresets.UrgentDamping, animator.Parameters!.DampingRatio, 2);
        animator.Settle();

        controller.RequestCollapse();
        animator.Settle();
        Assert.Equal(IslandState.Closed, controller.State);

        // Republiée sous le même identifiant : elle ne réclame plus l'attention.
        activities.PostActivity(Music(ActivityPriority.High, "Good Days (live)"));
        Assert.Equal(IslandState.Closed, controller.State);
    }

    [Fact]
    public void Tirer_pour_ouvrir_part_de_la_forme_etiree_avec_l_elan()
    {
        (IslandController controller, FakeAnimator animator, ActivityManager activities) = Create();
        activities.PostActivity(Music());
        var stretched = new IslandFootprint(110, 46);

        controller.OpenFromPull(stretched, 640, controller.ToggleFromUser);

        Assert.Equal(IslandState.Expanding, controller.State);
        Assert.Equal(640, animator.HeightVelocity, 6);
        Assert.Equal(stretched, animator.Current);
    }

    [Fact]
    public void Tirer_sans_rien_a_ouvrir_fait_retomber_la_forme()
    {
        (IslandController controller, FakeAnimator animator, _) = Create();

        controller.OpenFromPull(new IslandFootprint(110, 46), 640, () => { });

        Assert.Equal(IslandState.Closed, controller.State);
        Assert.True(animator.IsRunning);
        Assert.Equal(Rest, animator.Target);
    }

    [Fact]
    public void Sans_animations_la_forme_se_pose_et_l_etat_se_stabilise_aussitot()
    {
        (IslandController controller, FakeAnimator animator, ActivityManager activities) = Create(springs: false);
        activities.PostActivity(Music());

        controller.RequestExpand();

        Assert.Equal(IslandState.Expanded, controller.State);
        Assert.False(animator.IsRunning);
    }

    [Fact]
    public void Une_cible_de_depot_garde_la_forme_jusqu_au_depart_du_fichier()
    {
        (IslandController controller, FakeAnimator animator, _) = Create();
        var drop = new IslandFootprint(220, 60);

        controller.BeginDragTarget(drop);
        Assert.Equal(drop, animator.Target);

        controller.UpdateCollapsedFootprint(new IslandFootprint(130, 28));
        Assert.Equal(drop, animator.Target);

        controller.EndDragTarget();
        Assert.Equal(new IslandFootprint(130, 28), animator.Target);
    }

    [Fact]
    public void Ouverte_par_l_utilisateur_puis_d_elle_meme_SN07()
    {
        (IslandController controller, FakeAnimator animator, ActivityManager activities) = Create();

        activities.PostActivity(Music());
        controller.RequestExpand();
        animator.Settle();
        Assert.True(controller.OpenedByUser);

        controller.RequestCollapse();
        animator.Settle();
        Assert.Equal(IslandState.Closed, controller.State);
        Assert.False(controller.OpenedByUser);

        // Une erreur qui s'ouvre d'elle-même n'est pas « ouverte par l'utilisateur » :
        // elle pourra expirer.
        activities.PostActivity(new IslandActivity
        {
            Id = "error",
            FeatureId = "feature.agents",
            SceneKey = IslandSceneCatalog.Card,
            Title = "Échec",
            Priority = ActivityPriority.High
        });
        Assert.False(controller.OpenedByUser);
    }
}
