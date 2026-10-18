using System;
using System.Collections.Generic;
using SpaceNotch.Core.Animation;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Phase D de l'audit d'octobre : une seule horloge d'images, des contours
/// mis en cache.
/// </summary>
public sealed class PhaseDTests
{
    // ---------------- Horloge unique ----------------

    [Fact]
    public void L_horloge_chronometre_chaque_abonne_quand_on_le_lui_demande()
    {
        var fan = new FrameFanOut(() => { }, () => { });
        var timed = new List<(EventHandler<object> Handler, double Ms)>();
        EventHandler<object> slow = (_, _) => System.Threading.Thread.Sleep(20);
        EventHandler<object> quick = (_, _) => { };
        fan.Add(slow);
        fan.Add(quick);

        fan.Raise(null, new object());
        Assert.Empty(timed);

        fan.Timed = (handler, elapsed, _) => timed.Add((handler, elapsed.TotalMilliseconds));
        fan.Raise(null, new object());

        Assert.Equal(2, timed.Count);
        Assert.Same(slow, timed[0].Handler);
        Assert.True(timed[0].Ms >= 15);
        Assert.Same(quick, timed[1].Handler);
    }

    [Fact]
    public void L_horloge_dit_ce_que_chaque_abonne_alloue()
    {
        // n° 46 : les GC des animations viennent d'allocations par image ; la
        // mesure doit dire quel abonné alloue.
        var fan = new FrameFanOut(() => { }, () => { });
        var allocated = new Dictionary<EventHandler<object>, long>();
        EventHandler<object> greedy = (_, _) => GC.KeepAlive(new byte[64 * 1024]);
        EventHandler<object> frugal = (_, _) => { };
        fan.Add(greedy);
        fan.Add(frugal);

        fan.Timed = (handler, _, bytes) => allocated[handler] = bytes;
        fan.Raise(null, new object());

        Assert.True(allocated[greedy] >= 64 * 1024);
        Assert.True(allocated[frugal] < 1024);
    }

    [Fact]
    public void Un_seul_abonnement_reel_pose_au_premier_et_retire_au_dernier()
    {
        int attached = 0;
        int detached = 0;
        var fan = new FrameFanOut(() => attached++, () => detached++);

        EventHandler<object> a = (_, _) => { };
        EventHandler<object> b = (_, _) => { };

        fan.Add(a);
        fan.Add(b);
        Assert.Equal(1, attached);
        Assert.True(fan.IsAttached);

        fan.Remove(a);
        Assert.Equal(0, detached);

        fan.Remove(b);
        Assert.Equal(1, detached);
        Assert.False(fan.IsAttached);

        // Retirer ce qui n'est pas là ne fait rien.
        fan.Remove(a);
        Assert.Equal(1, detached);
    }

    [Fact]
    public void Chaque_abonne_est_appele_une_fois_par_image()
    {
        var fan = new FrameFanOut(() => { }, () => { });
        var calls = new List<string>();

        EventHandler<object>? b = null;
        EventHandler<object> late = (_, _) => calls.Add("late");
        EventHandler<object> a = (_, _) =>
        {
            calls.Add("a");

            // Retiré par un autre pendant l'image : il n'est plus appelé.
            fan.Remove(b);

            // Ajouté pendant l'image : appelé à la suivante.
            fan.Add(late);
        };
        b = (_, _) => calls.Add("b");

        fan.Add(a);
        fan.Add(b);
        fan.Raise(null, new object());
        Assert.Equal(["a"], calls);

        calls.Clear();
        fan.Remove(a);
        fan.Raise(null, new object());
        Assert.Equal(["late"], calls);
    }

    [Fact]
    public void Un_abonne_qui_se_retire_lui_meme_ne_casse_pas_l_image()
    {
        var fan = new FrameFanOut(() => { }, () => { });
        int count = 0;
        EventHandler<object>? self = null;
        self = (_, _) =>
        {
            count++;
            fan.Remove(self);
        };

        fan.Add(self);
        fan.Raise(null, new object());
        fan.Raise(null, new object());

        Assert.Equal(1, count);
        Assert.Equal(0, fan.Count);
    }

    [Fact]
    public void Un_branchement_rate_ne_laisse_pas_l_horloge_se_croire_branchee()
    {
        // v1.16.0 : un abonnement venu d'un autre fil faisait échouer le
        // branchement, mais l'horloge se croyait branchée — toutes les
        // animations restaient figées, la notch vide et sans yeux.
        bool refuse = true;
        int attached = 0;
        var fan = new FrameFanOut(
            () =>
            {
                if (refuse)
                {
                    throw new InvalidOperationException("mauvais fil");
                }

                attached++;
            },
            () => { });

        EventHandler<object> a = (_, _) => { };
        Assert.Throws<InvalidOperationException>(() => fan.Add(a));
        Assert.False(fan.IsAttached);
        Assert.Equal(0, fan.Count);

        refuse = false;
        fan.Add(a);
        Assert.True(fan.IsAttached);
        Assert.Equal(1, attached);
    }

    [Fact]
    public void Une_animation_qui_echoue_est_retiree_sans_arreter_les_autres()
    {
        var errors = new List<Exception>();
        var fan = new FrameFanOut(() => { }, () => { }, errors.Add);
        int frames = 0;

        EventHandler<object> broken = (_, _) => throw new InvalidOperationException("cassée");
        EventHandler<object> healthy = (_, _) => frames++;

        fan.Add(broken);
        fan.Add(healthy);
        fan.Raise(null, new object());
        fan.Raise(null, new object());

        Assert.Equal(2, frames);
        Assert.Single(errors);
        Assert.Equal(1, fan.Count);
    }

    [Fact]
    public void Un_proprietaire_dont_l_abonne_a_echoue_peut_se_reabonner()
    {
        // Les propriétaires de l'horloge (ressort, détachement, effets de forme,
        // compteur, teinte) gardent un drapeau « je tourne » pour ne pas
        // s'abonner deux fois. L'abonné en échec est retiré, mais le drapeau
        // restait vrai : le démarrage suivant sortait tôt et l'animation ne
        // repartait plus avant le redémarrage de l'application.
        var fan = new FrameFanOut(() => { }, () => { });
        var owner = new FrameOwner(fan);

        owner.Start();
        owner.Fails = true;
        fan.Raise(null, new object());
        Assert.Equal(0, fan.Count);

        owner.Fails = false;
        owner.Start();
        fan.Raise(null, new object());

        Assert.Equal(1, owner.Frames);
    }

    [Fact]
    public void Le_rappel_d_echec_n_est_pas_appele_quand_l_abonne_se_retire_normalement()
    {
        var fan = new FrameFanOut(() => { }, () => { });
        int failed = 0;
        EventHandler<object> a = (_, _) => { };

        fan.Add(a, () => failed++);
        fan.Remove(a);
        fan.Raise(null, new object());

        Assert.Equal(0, failed);
    }

    [Fact]
    public void Le_rappel_d_echec_est_appele_apres_le_retrait_et_ne_vise_que_l_abonne_en_echec()
    {
        var fan = new FrameFanOut(() => { }, () => { });
        var failed = new List<string>();
        int countSeenByCallback = -1;
        EventHandler<object> broken = (_, _) => throw new InvalidOperationException("cassée");
        EventHandler<object> healthy = (_, _) => { };

        // L'ordre d'ajout n'est pas l'ordre de retrait : les rappels suivent leur abonné.
        fan.Add(healthy, () => failed.Add("saine"));
        fan.Add(broken, () =>
        {
            failed.Add("cassée");
            countSeenByCallback = fan.Count;
        });
        fan.Raise(null, new object());
        fan.Raise(null, new object());

        Assert.Equal(["cassée"], failed);
        Assert.Equal(1, countSeenByCallback);
        Assert.Equal(1, fan.Count);
    }

    [Fact]
    public void Un_rappel_d_echec_qui_leve_une_erreur_ne_prive_pas_les_autres_de_leur_image()
    {
        var errors = new List<Exception>();
        var fan = new FrameFanOut(() => { }, () => { }, errors.Add);
        int frames = 0;
        EventHandler<object> broken = (_, _) => throw new InvalidOperationException("cassée");
        EventHandler<object> healthy = (_, _) => frames++;

        fan.Add(broken, () => throw new InvalidOperationException("rappel cassé"));
        fan.Add(healthy);
        fan.Raise(null, new object());

        Assert.Equal(1, frames);
        Assert.Equal(2, errors.Count);

        // Le journal doit pouvoir dire que c'est le rappel qui a échoué.
        Assert.IsType<InvalidOperationException>(errors[1]);
        Assert.Equal("rappel cassé", errors[1].InnerException?.Message);
    }

    [Fact]
    public void Un_abonne_qui_se_reabonne_puis_echoue_ne_laisse_ni_inscription_ni_drapeau()
    {
        // Fin de course d'un ressort : il s'arrête, la fin relance une animation
        // (nouvelle inscription du même abonné), puis l'erreur tombe dans ce même appel.
        var fan = new FrameFanOut(() => { }, () => { });
        bool running = false;
        EventHandler<object>? frame = null;

        void Start()
        {
            if (running)
            {
                return;
            }

            running = true;
            fan.Add(frame, () => running = false);
        }

        frame = (_, _) =>
        {
            fan.Remove(frame);
            running = false;
            Start();
            throw new InvalidOperationException("cassée");
        };

        Start();
        fan.Raise(null, new object());

        Assert.Equal(0, fan.Count);
        Assert.False(running);
    }

    [Fact]
    public void Un_proprietaire_peut_se_reabonner_dans_son_propre_rappel_d_echec()
    {
        // Redémarrer tout de suite : l'abonné neuf est appelé à l'image suivante,
        // jamais dans celle qui l'a vu échouer (même règle qu'un ajout en cours d'image).
        var fan = new FrameFanOut(() => { }, () => { });
        int calls = 0;
        EventHandler<object>? frame = null;
        frame = (_, _) =>
        {
            calls++;

            if (calls == 1)
            {
                throw new InvalidOperationException("cassée");
            }
        };

        void Start() => fan.Add(frame, Start);

        Start();
        fan.Raise(null, new object());
        Assert.Equal(1, calls);
        Assert.Equal(1, fan.Count);

        fan.Raise(null, new object());
        Assert.Equal(2, calls);
    }

    /// <summary>Un propriétaire d'animation tel que ceux de l'application : un drapeau, un abonné.</summary>
    private sealed class FrameOwner(FrameFanOut fan)
    {
        private bool _running;

        public bool Fails { get; set; }

        public int Frames { get; private set; }

        public void Start()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            fan.Add(OnFrame, () => _running = false);
        }

        private void OnFrame(object? sender, object e)
        {
            if (Fails)
            {
                throw new InvalidOperationException("cassée");
            }

            Frames++;
        }
    }

    // ---------------- Cache des contours ----------------

    [Fact]
    public void Les_tailles_proches_au_quart_de_DIP_partagent_un_contour()
    {
        var cache = new SilhouetteCache();

        ShapePoint[] first = cache.Silhouette(200.01, 60.02, 18, 4, 0, 12);
        ShapePoint[] second = cache.Silhouette(199.98, 59.99, 18, 4, 0, 12);

        Assert.Same(first, second);
        Assert.Equal(1, cache.Misses);
        Assert.Equal(1, cache.Hits);

        // Le contour mis en cache est celui de la taille arrondie.
        Assert.Equal(IslandShape.Silhouette(200, 60, 18, 4, 0, 12), first);
    }

    [Fact]
    public void Le_cache_garde_les_plus_recents_et_oublie_les_plus_anciens()
    {
        var cache = new SilhouetteCache(capacity: 2);

        ShapePoint[] a = cache.Silhouette(100, 30, 10, 4, 0, 8);
        _ = cache.Silhouette(110, 30, 10, 4, 0, 8);
        _ = cache.Silhouette(100, 30, 10, 4, 0, 8); // a redevient le plus récent
        _ = cache.Silhouette(120, 30, 10, 4, 0, 8); // 110 est oublié

        Assert.Equal(2, cache.Count);
        Assert.Same(a, cache.Silhouette(100, 30, 10, 4, 0, 8));

        long misses = cache.Misses;
        _ = cache.Silhouette(110, 30, 10, 4, 0, 8);
        Assert.Equal(misses + 1, cache.Misses);
    }

    [Fact]
    public void Pastille_et_forme_accrochee_ne_se_confondent_pas()
    {
        var cache = new SilhouetteCache();

        Assert.NotSame(cache.Silhouette(100, 40, 20, 4, 0, 0), cache.Floating(100, 40, 20, 4));
        Assert.Equal(0.25, SilhouetteCache.Snap(0.2), 6);
        Assert.Equal(10.5, SilhouetteCache.Snap(10.4), 6);
    }
}
