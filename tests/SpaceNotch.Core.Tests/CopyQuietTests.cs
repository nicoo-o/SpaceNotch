using System;
using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Features.Clipboard;
using SpaceNotch.Platform.Windows.Clipboard;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Copie discrète (session du 2026-10-04) : une copie fait apparaître un signal
/// bref, puis la notch revient au repos ; la copie attend dans la pile, jamais
/// présentée d'office. Décisions : signal 2,5 s, rafale 1 s, réécriture 500 ms.
/// </summary>
public sealed class CopyQuietTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static IslandActivity Entry(string id, ActivityPresentationPolicy? policy = null, ActivityPriority priority = ActivityPriority.Normal, DateTimeOffset? createdAt = null)
        => new() { Id = id, FeatureId = "test", SceneKey = IslandSceneCatalog.Card, Title = id, Priority = priority, Policy = policy, CreatedAt = createdAt ?? Start };

    // ---------------- Politique « en pile seulement » ----------------

    [Fact]
    public void Une_entree_en_pile_seulement_n_est_jamais_presentee_d_office()
    {
        var manager = new ActivityManager(() => Start);
        manager.PostActivity(Entry("copie", ActivityPresentationPolicy.Listed));

        Assert.Null(manager.CurrentActivity);
        Assert.Single(manager.GetActiveActivities());
    }

    [Fact]
    public void La_molette_au_repos_montre_la_premiere_entree_en_pile()
    {
        var manager = new ActivityManager(() => Start);
        manager.PostActivity(Entry("copie", ActivityPresentationPolicy.Listed));

        Assert.True(manager.CyclePresentation(1));
        Assert.Equal("copie", manager.CurrentActivity?.Id);
    }

    [Fact]
    public void Depuis_le_repos_la_molette_commence_par_la_premiere_entree()
    {
        var manager = new ActivityManager(() => Start);
        manager.PostActivity(Entry("ancienne", ActivityPresentationPolicy.Listed, createdAt: Start));
        manager.PostActivity(Entry("recente", ActivityPresentationPolicy.Listed, createdAt: Start.AddSeconds(1)));

        manager.CyclePresentation(1);
        Assert.Equal("recente", manager.CurrentActivity?.Id);
    }

    [Fact]
    public void Une_activite_ordinaire_passe_devant_une_entree_en_pile()
    {
        var manager = new ActivityManager(() => Start);
        manager.PostActivity(Entry("copie", ActivityPresentationPolicy.Listed, ActivityPriority.High));
        manager.PostActivity(Entry("musique"));

        Assert.Equal("musique", manager.CurrentActivity?.Id);
    }

    [Fact]
    public void Une_epingle_choisie_par_l_utilisateur_reste_devant_un_signal_ordinaire()
    {
        var manager = new ActivityManager(() => Start);
        manager.PostActivity(Entry("minuteur"));
        manager.PostActivity(Entry("musique"));
        manager.PinPresentation("minuteur");

        manager.PostActivity(Entry("signal", ActivityPresentationPolicy.Temporary));
        Assert.Equal("minuteur", manager.CurrentActivity?.Id);
    }

    [Fact]
    public void Une_entree_en_pile_montree_par_la_molette_rend_la_main_apres_8_s()
    {
        DateTimeOffset now = Start;
        var manager = new ActivityManager(() => now);
        manager.PostActivity(Entry("copie", ActivityPresentationPolicy.Listed));
        manager.CyclePresentation(1);

        now += TimeSpan.FromSeconds(7);
        Assert.Equal(TimeSpan.FromSeconds(1), manager.GetTimeUntilNextExpiration(now));
        manager.ExpireOverdue(now);
        Assert.Equal("copie", manager.CurrentActivity?.Id);

        now += TimeSpan.FromSeconds(2);
        manager.ExpireOverdue(now);
        Assert.Null(manager.CurrentActivity);
        Assert.Single(manager.GetActiveActivities());
    }

    [Fact]
    public void Le_bail_ne_ferme_pas_une_entree_que_l_utilisateur_regarde()
    {
        DateTimeOffset now = Start;
        var manager = new ActivityManager(() => now);
        manager.PostActivity(Entry("copie", ActivityPresentationPolicy.Listed));
        manager.CyclePresentation(1);

        now += TimeSpan.FromSeconds(9);
        manager.ExpireOverdue(now, spare: "copie");
        Assert.Equal("copie", manager.CurrentActivity?.Id);
    }

    // ---------------- Bulle et points ----------------

    [Fact]
    public void Au_repos_une_entree_en_pile_ne_fait_pas_de_point()
    {
        IslandActivity entry = Entry("copie", ActivityPresentationPolicy.Listed);

        Assert.Equal(0, SplitPresentation.HiddenCount(null, [entry], null));
    }

    [Fact]
    public void Sous_une_activite_l_entree_en_pile_compte_comme_un_point()
    {
        IslandActivity music = Entry("musique");
        IslandActivity entry = Entry("copie", ActivityPresentationPolicy.Listed);

        Assert.Equal(1, SplitPresentation.HiddenCount(music, [music, entry], null));
    }

    [Fact]
    public void Un_signal_par_dessus_la_musique_compte_la_copie_pas_la_musique()
    {
        // La notch retrouvera la musique après le signal : c'est elle qui ne
        // compte pas ; la copie, elle, reste cachée dans la pile.
        IslandActivity music = Entry("musique", createdAt: Start);
        IslandActivity entry = Entry("copie", ActivityPresentationPolicy.Listed, createdAt: Start.AddSeconds(1));
        IslandActivity signal = Entry("signal", ActivityPresentationPolicy.Temporary, createdAt: Start.AddSeconds(1));

        Assert.Equal(1, SplitPresentation.HiddenCount(signal, [signal, music, entry], null));
    }

    [Fact]
    public void Une_entree_en_pile_ne_devient_jamais_la_bulle()
    {
        IslandActivity download = new()
        {
            Id = "telechargement",
            FeatureId = "test",
            SceneKey = IslandSceneCatalog.Card,
            Title = "fichier.zip",
            Role = ActivityRole.Download,
            CreatedAt = Start
        };
        IslandActivity entry = Entry("copie", ActivityPresentationPolicy.Listed, createdAt: Start.AddSeconds(1));

        Assert.Null(SplitPresentation.BubbleFor(download, [download, entry]));
    }

    // ---------------- Presse-papier ----------------

    private static (ClipboardFeature Feature, ActivityManager Manager, Func<TimeSpan, DateTimeOffset> Advance) Build()
    {
        DateTimeOffset now = Start;
        var manager = new ActivityManager(() => now);
        var feature = new ClipboardFeature(manager, new EventBus(), new ClipboardMonitor(), IntPtr.Zero, isEnabled: true, now: () => now);
        return (feature, manager, delta => now += delta);
    }

    [Fact]
    public void Une_rafale_de_copies_ne_produit_qu_un_signal_qui_compte()
    {
        (ClipboardFeature feature, ActivityManager manager, Func<TimeSpan, DateTimeOffset> advance) = Build();

        feature.Capture("un");
        advance(TimeSpan.FromMilliseconds(600));
        feature.Capture("deux");
        advance(TimeSpan.FromMilliseconds(600));
        feature.Capture("trois");

        IslandActivity signal = manager.GetActiveActivities().Single(a => a.Id == ClipboardFeature.SignalActivityId);
        Assert.Contains("3", signal.Title, StringComparison.Ordinal);
        Assert.Equal(ClipboardFeature.SignalActivityId, manager.CurrentActivity?.Id);
    }

    [Fact]
    public void Plus_d_une_seconde_entre_deux_copies_recommence_le_compte()
    {
        (ClipboardFeature feature, ActivityManager manager, Func<TimeSpan, DateTimeOffset> advance) = Build();

        feature.Capture("un");
        advance(TimeSpan.FromMilliseconds(1500));
        feature.Capture("deux");

        IslandActivity signal = manager.GetActiveActivities().Single(a => a.Id == ClipboardFeature.SignalActivityId);
        Assert.DoesNotContain("2", signal.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Deux_secondes_et_demie_apres_la_derniere_copie_la_notch_revient_au_repos()
    {
        (ClipboardFeature feature, ActivityManager manager, Func<TimeSpan, DateTimeOffset> advance) = Build();
        feature.Capture("texte");

        manager.ExpireOverdue(advance(TimeSpan.FromMilliseconds(2400)));
        Assert.Equal(ClipboardFeature.SignalActivityId, manager.CurrentActivity?.Id);

        manager.ExpireOverdue(advance(TimeSpan.FromMilliseconds(200)));
        Assert.Null(manager.CurrentActivity);
        Assert.Contains(manager.GetActiveActivities(), a => a.Id == ClipboardFeature.ActivityId);
    }

    [Fact]
    public void Une_reecriture_dans_les_500_ms_remplace_l_entree_au_lieu_d_en_ajouter_une()
    {
        (ClipboardFeature feature, ActivityManager manager, Func<TimeSpan, DateTimeOffset> advance) = Build();
        feature.Capture("https://exemple.be/?utm_source=x");
        advance(TimeSpan.FromMilliseconds(120));
        feature.Capture("https://exemple.be/");

        Assert.Equal(1, feature.EntryCount);
        IslandActivity signal = manager.GetActiveActivities().Single(a => a.Id == ClipboardFeature.SignalActivityId);
        Assert.DoesNotContain("2", signal.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_selection_qui_grandit_reste_une_seule_entree()
    {
        // Copie à la sélection (mod Windhawk) : chaque cran de la sélection
        // recopie un texte qui prolonge le précédent.
        (ClipboardFeature feature, _, Func<TimeSpan, DateTimeOffset> advance) = Build();

        foreach (string text in new[] { "Peux", "Peux-tu", "Peux-tu relire" })
        {
            feature.Capture(text);
            advance(TimeSpan.FromMilliseconds(150));
        }

        Assert.Equal(1, feature.EntryCount);
    }

    [Fact]
    public void Cinq_vraies_copies_a_300_ms_restent_cinq_entrees()
    {
        (ClipboardFeature feature, ActivityManager manager, Func<TimeSpan, DateTimeOffset> advance) = Build();

        foreach (string text in new[] { "un", "deux", "trois", "quatre", "cinq" })
        {
            feature.Capture(text);
            advance(TimeSpan.FromMilliseconds(300));
        }

        Assert.Equal(5, feature.EntryCount);
        Assert.Contains("5", manager.GetActiveActivities().Single(a => a.Id == ClipboardFeature.SignalActivityId).Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_copie_pendant_la_musique_la_recouvre_puis_la_rend()
    {
        (ClipboardFeature feature, ActivityManager manager, Func<TimeSpan, DateTimeOffset> advance) = Build();
        manager.PostActivity(new IslandActivity { Id = "media", FeatureId = "media", SceneKey = IslandSceneCatalog.Media, Title = "Morceau", CreatedAt = Start });

        // La copie arrive pendant que la musique joue, donc après elle.
        advance(TimeSpan.FromSeconds(1));
        feature.Capture("texte");
        Assert.Equal(ClipboardFeature.SignalActivityId, manager.CurrentActivity?.Id);

        manager.ExpireOverdue(advance(TimeSpan.FromSeconds(3)));
        Assert.Equal("media", manager.CurrentActivity?.Id);
    }

    [Fact]
    public void Une_entree_en_pile_qui_arrive_ne_reclame_rien()
    {
        IslandActivity presented = Entry("musique");
        IslandActivity entry = Entry("copie", ActivityPresentationPolicy.Listed);

        Assert.Equal(ActivityInterruption.Ignore, ActivityPolicies.Decide(presented, entry, NotchPresentation.Compact));
    }

    [Fact]
    public void Notch_ouverte_le_signal_attend_la_fermeture()
    {
        IslandActivity opened = Entry("musique");
        IslandActivity signal = Entry("signal", ActivityPresentationPolicy.Temporary);

        Assert.Equal(ActivityInterruption.Queue, ActivityPolicies.Decide(opened, signal, NotchPresentation.Expanded));
    }
}
