using System.Linq;
using SpaceNotch.Core.Machine;
using SpaceNotch.Core.State;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// La machine à états de la notch (phase E) : chaque région, chaque garde.
/// </summary>
public sealed class NotchMachineTests
{
    private static NotchMachine Machine(NotchRules? rules = null) => new(rules);

    private static NotchInput In(NotchTrigger trigger) => new(trigger);

    [Fact]
    public void Le_survol_pose_montre_l_apercu_et_la_sortie_le_retire()
    {
        NotchMachine m = Machine();

        Assert.Contains(NotchEffect.ShowPreview, m.Fire(In(NotchTrigger.HoverDwell)).Effects);
        Assert.Equal(Surface.Preview, m.Current.Surface);

        Assert.Contains(NotchEffect.EndPreview, m.Fire(In(NotchTrigger.HoverLeave)).Effects);
        Assert.Equal(Surface.Rest, m.Current.Surface);
    }

    [Fact]
    public void Sans_apercu_au_survol_le_survol_ne_fait_rien()
    {
        NotchMachine m = Machine(new NotchRules(HoverToPreview: false));

        Assert.Empty(m.Fire(In(NotchTrigger.HoverDwell)).Effects);
        Assert.Equal(Surface.Rest, m.Current.Surface);
    }

    [Fact]
    public void Un_clic_ouvre_et_prend_le_clavier_un_second_referme_et_le_rend()
    {
        NotchMachine m = Machine();

        m.Fire(In(NotchTrigger.Press));
        NotchStep open = m.Fire(In(NotchTrigger.Release));
        Assert.Equal([NotchEffect.Open, NotchEffect.CaptureKeyboard], open.Effects);
        Assert.Equal(new NotchSnapshot(Presence.Visible, Placement.Attached, HandPhase.Free, Surface.Open, KeyboardState.Captured), m.Current);

        m.Fire(In(NotchTrigger.Press));
        NotchStep close = m.Fire(In(NotchTrigger.Release));
        Assert.Equal([NotchEffect.Close, NotchEffect.ReleaseKeyboard], close.Effects);
        Assert.Equal(NotchSnapshot.Initial, m.Current);
    }

    [Fact]
    public void Echap_et_clic_ailleurs_referment_seulement_une_notch_ouverte()
    {
        NotchMachine m = Machine();
        Assert.Empty(m.Fire(In(NotchTrigger.Escape)).Effects);

        m.Fire(In(NotchTrigger.HotKey));
        Assert.Contains(NotchEffect.Close, m.Fire(In(NotchTrigger.ClickOutside)).Effects);
        Assert.Equal(KeyboardState.Free, m.Current.Keyboard);
    }

    [Fact]
    public void Main_occupee_pas_d_apercu()
    {
        NotchMachine m = Machine();

        m.Fire(In(NotchTrigger.Press));
        Assert.Empty(m.Fire(In(NotchTrigger.HoverDwell)).Effects);
        Assert.Equal(Surface.Rest, m.Current.Surface);
    }

    [Fact]
    public void Tirer_au_dela_du_seuil_ouvre_au_lacher()
    {
        NotchMachine m = Machine();

        m.Fire(In(NotchTrigger.Press));
        m.Fire(new NotchInput(NotchTrigger.PullMoved, PullDip: 20));
        Assert.Equal(HandPhase.Pulling, m.Current.Hand);

        NotchStep step = m.Fire(new NotchInput(NotchTrigger.Release, PullDip: 20));
        Assert.Contains(NotchEffect.Open, step.Effects);
        Assert.Equal(Surface.Open, m.Current.Surface);
    }

    [Fact]
    public void Tirer_un_peu_puis_lacher_fait_revenir_la_forme()
    {
        NotchMachine m = Machine();

        m.Fire(In(NotchTrigger.Press));
        m.Fire(new NotchInput(NotchTrigger.PullMoved, PullDip: 9));
        NotchStep step = m.Fire(new NotchInput(NotchTrigger.Release, PullDip: 9, LateralDip: 20));

        Assert.Equal([NotchEffect.SpringBack], step.Effects);
        Assert.Equal(Surface.Rest, m.Current.Surface);
    }

    [Fact]
    public void Tirer_jusqu_a_T_arrache_la_notch_mais_pas_sans_detachement()
    {
        NotchMachine m = Machine();
        m.Fire(In(NotchTrigger.Press));
        m.Fire(new NotchInput(NotchTrigger.PullMoved, PullDip: 10));
        Assert.Contains(NotchEffect.Tear, m.Fire(new NotchInput(NotchTrigger.PullMoved, PullDip: 41)).Effects);
        Assert.Equal(Placement.Floating, m.Current.Placement);

        NotchMachine stuck = Machine(new NotchRules(AllowDetach: false));
        stuck.Fire(In(NotchTrigger.Press));
        stuck.Fire(new NotchInput(NotchTrigger.PullMoved, PullDip: 10));
        Assert.Empty(stuck.Fire(new NotchInput(NotchTrigger.PullMoved, PullDip: 90)).Effects);
        Assert.Contains(NotchEffect.Open, stuck.Fire(new NotchInput(NotchTrigger.Release, PullDip: 90)).Effects);
    }

    [Fact]
    public void Flottante_la_main_deplace_sans_jamais_tirer()
    {
        NotchMachine m = Machine();
        m.Fire(new NotchInput(NotchTrigger.PlacementChanged, Placement: Placement.Floating));

        m.Fire(In(NotchTrigger.Press));
        m.Fire(new NotchInput(NotchTrigger.PullMoved, PullDip: 60));

        Assert.Equal(HandPhase.Dragging, m.Current.Hand);
        Assert.Empty(m.Fire(new NotchInput(NotchTrigger.Release, PullDip: 60)).Effects);
    }

    [Fact]
    public void Un_appui_long_au_doigt_ouvre_le_menu_rapide()
    {
        NotchMachine m = Machine();

        m.Fire(In(NotchTrigger.Press));
        NotchStep step = m.Fire(new NotchInput(NotchTrigger.Release, Device: PointerKind.Touch, HeldSeconds: 0.6));

        Assert.Equal([NotchEffect.OpenQuickMenu], step.Effects);
        Assert.Equal(Surface.Rest, m.Current.Surface);
    }

    [Fact]
    public void Une_activite_importante_ouvre_sans_prendre_le_clavier()
    {
        NotchMachine m = Machine();

        Assert.Equal([NotchEffect.Open], m.Fire(new NotchInput(NotchTrigger.ActivityArrived, ClaimsAttention: true)).Effects);
        Assert.Equal(KeyboardState.Free, m.Current.Keyboard);

        Assert.Contains(NotchEffect.Close, m.Fire(In(NotchTrigger.ActivitiesEmptied)).Effects);
    }

    [Fact]
    public void Retiree_la_notch_fige_tout_rend_le_clavier_et_reprend_au_retour()
    {
        NotchMachine m = Machine();
        m.Fire(In(NotchTrigger.HotKey));

        NotchStep hide = m.Fire(new NotchInput(NotchTrigger.PresenceChanged, Presence: Presence.Withdrawn));
        Assert.Equal([NotchEffect.SuspendLife, NotchEffect.ReleaseKeyboard], hide.Effects);

        // Figée : aucune entrée n'agit, même une activité qui disparaît.
        Assert.Empty(m.Fire(In(NotchTrigger.ActivitiesEmptied)).Effects);
        Assert.Equal(Surface.Open, m.Current.Surface);

        Assert.Equal([NotchEffect.ResumeLife], m.Fire(new NotchInput(NotchTrigger.PresenceChanged, Presence: Presence.Visible)).Effects);
    }

    [Fact]
    public void L_ombre_compte_les_ecarts_et_se_realigne()
    {
        var shadow = new NotchShadow();

        shadow.Feed(In(NotchTrigger.HoverDwell));
        Assert.Null(shadow.Compare(IslandState.Preview, "survol"));

        string? divergence = shadow.Compare(IslandState.Expanded, "ouverture");
        Assert.NotNull(divergence);
        Assert.StartsWith("[OMBRE]", divergence);
        Assert.Equal(1, shadow.Divergences);

        shadow.Resync(IslandState.Expanded);
        Assert.Null(shadow.Compare(IslandState.Expanding, "ouverture"));
        Assert.Equal(3, shadow.Checks);
        Assert.Contains("1 écart", shadow.Summary);

        Assert.Equal(Surface.Rest, NotchShadow.SurfaceOf(IslandState.Collapsing));
        Assert.True(new[] { IslandState.Closed, IslandState.Collapsing }.All(s => NotchShadow.SurfaceOf(s) == Surface.Rest));
    }
}
