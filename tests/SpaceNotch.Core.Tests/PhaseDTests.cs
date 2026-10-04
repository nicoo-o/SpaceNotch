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

        fan.Timed = (handler, elapsed) => timed.Add((handler, elapsed.TotalMilliseconds));
        fan.Raise(null, new object());

        Assert.Equal(2, timed.Count);
        Assert.Same(slow, timed[0].Handler);
        Assert.True(timed[0].Ms >= 15);
        Assert.Same(quick, timed[1].Handler);
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
