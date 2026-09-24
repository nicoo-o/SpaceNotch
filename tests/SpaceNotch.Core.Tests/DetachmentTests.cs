using System;
using System.Collections.Generic;
using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Animation;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// La notch qu'on arrache au bord : résistance, élan, aimants, goutte qui se
/// rompt, ouverture vers l'espace libre et bulle partagée. Voir ADR-019.
/// </summary>
public class DetachmentTests
{
    private const double Tolerance = 0.001;

    private static readonly ScreenRect Work = new(0, 0, 1920, 1040);

    // ------------------------------------------------------------------
    // Physique du geste
    // ------------------------------------------------------------------

    [Fact]
    public void RubberBand_StartsWithTheUIScrollViewSlopeAndNeverReachesItsDimension()
    {
        double small = FluidMotion.RubberBand(1, 90);
        Assert.InRange(small, 0.54, 0.55);

        double previous = 0;

        foreach (double offset in new[] { 10.0, 40, 100, 400, 4000 })
        {
            double resisted = FluidMotion.RubberBand(offset, 90);

            Assert.True(resisted > previous, "La résistance doit rester monotone.");
            Assert.True(resisted < 90, "La matière ne dépasse jamais sa dimension.");
            Assert.True(resisted < offset, "Tirer donne toujours moins que la main.");
            previous = resisted;
        }

        Assert.Equal(-FluidMotion.RubberBand(40, 90), FluidMotion.RubberBand(-40, 90), 9);
        Assert.Equal(0, FluidMotion.RubberBand(0, 90));
    }

    [Fact]
    public void Projection_UsesExponentialDecayLikeAScrollView()
    {
        // 1 000 DIPs/s à la décélération normale : 0,998 / 0,002 = 499 DIPs.
        Assert.Equal(499, FluidMotion.Project(1000), 6);
        Assert.Equal(-499, FluidMotion.Project(-1000), 6);
        Assert.True(FluidMotion.Project(1000, FluidMotion.FastDeceleration) < FluidMotion.Project(1000));
        Assert.Equal(0, FluidMotion.Project(double.NaN));
    }

    [Theory]
    [InlineData(2800, 0)]
    [InlineData(0, -1500)]
    [InlineData(900, 900)]
    [InlineData(12000, 300)]
    public void Stretch_PreservesAreaAndStaysSubtle(double vx, double vy)
    {
        (double sx, double sy) = FluidMotion.Stretch(vx, vy);

        Assert.Equal(1, sx * sy, 9);
        Assert.InRange(sx, 1 / 1.05 - Tolerance, 1.05 + Tolerance);
        Assert.InRange(sy, 1 / 1.05 - Tolerance, 1.05 + Tolerance);
    }

    [Fact]
    public void Stretch_ElongatesAlongTheMotion()
    {
        (double sx, double sy) = FluidMotion.Stretch(2800, 0);
        Assert.Equal(1.05, sx, 6);
        Assert.True(sy < 1);

        (sx, sy) = FluidMotion.Stretch(0, 2800);
        Assert.True(sy > 1 && sx < 1);

        Assert.Equal((1.0, 1.0), FluidMotion.Stretch(0, 0));
    }

    [Fact]
    public void Spring2D_FollowsAMovingTargetOvershootsOnceAndSettles()
    {
        var spring = new Spring2D(SpringParameters.FromResponse(0.28, 0.72));
        spring.SetTarget(200, 0);

        double peak = 0;
        int frames = 0;

        while (!spring.IsSettled && frames < 600)
        {
            spring.Step(1.0 / 120);
            peak = Math.Max(peak, spring.X);
            frames++;
        }

        Assert.True(spring.IsSettled, "Le ressort doit s'arrêter : aucun rendu au repos.");
        Assert.Equal(200, spring.X, 6);
        Assert.True(peak > 200.5, "Un rebond visible à l'arrêt.");
        Assert.True(peak < 220, "Un seul petit rebond, pas une gelée.");
    }

    [Fact]
    public void Spring2D_KeepsVelocityWhenTheTargetJumps()
    {
        var spring = new Spring2D(SpringParameters.FromResponse(0.28, 0.72));
        spring.SetTarget(100, 0);

        for (int i = 0; i < 6; i++)
        {
            spring.Step(1.0 / 120);
        }

        double velocity = spring.VelocityX;
        double position = spring.X;
        spring.SetTarget(-100, 0);

        Assert.Equal(velocity, spring.VelocityX);
        Assert.Equal(position, spring.X);
    }

    [Fact]
    public void Spring2D_SurvivesALongFrame()
    {
        var spring = new Spring2D(SpringParameters.FromResponse(0.2, 0.3));
        spring.SetTarget(500, 500);
        spring.Step(3);

        Assert.False(double.IsNaN(spring.X));
        Assert.InRange(spring.X, -2000, 3000);
    }

    [Fact]
    public void Spring1D_SettlesOnAScale()
    {
        var pop = new Spring1D(SpringParameters.FromResponse(0.35, 0.5), 0.94);
        pop.SetTarget(1);

        int frames = 0;
        double peak = 0;

        while (!pop.IsSettled && frames < 600)
        {
            pop.Step(1.0 / 120);
            peak = Math.Max(peak, pop.Value);
            frames++;
        }

        Assert.True(pop.IsSettled);
        Assert.True(frames > 20, "Une échelle ne se déclare pas au repos dès la première image.");
        Assert.True(peak > 1.0, "Le pop rebondit.");
    }

    // ------------------------------------------------------------------
    // Arrachement
    // ------------------------------------------------------------------

    [Fact]
    public void Pulling_ResistsAndOnlyDownwards()
    {
        Assert.Equal(0, Detachment.PullStretch(-30));
        Assert.Equal(0, Detachment.PullStretch(0));
        Assert.True(Detachment.PullStretch(40) < 40);
        Assert.True(Detachment.PullStretch(40) > 10);

        Assert.False(Detachment.ShouldTear(39.9));
        Assert.True(Detachment.ShouldTear(40));
    }

    [Fact]
    public void ASmallWiggleIsStillAClick()
    {
        Assert.False(Detachment.ExceedsClickSlop(3, 4));
        Assert.False(Detachment.ExceedsClickSlop(-6, 0));
        Assert.True(Detachment.ExceedsClickSlop(5, 5));
    }

    [Fact]
    public void APulledNotchGrowsTallerAndSlightlyNarrower()
    {
        var rest = new IslandFootprint(280, 52);
        IslandFootprint pulled = Detachment.Pulled(rest, 40);

        Assert.True(pulled.Height > rest.Height);
        Assert.True(pulled.Width < rest.Width);
        Assert.True(pulled.Width >= rest.Width * 0.82);
        Assert.Equal(rest, Detachment.Pulled(rest, -20));
    }

    [Fact]
    public void TheFloatingPillLeavesItsShouldersOnTheScreenEdge()
    {
        var rest = new IslandFootprint(280, 52);
        IslandFootprint pill = Detachment.FloatingOf(rest, NotchGeometry.DefaultShoulder);

        Assert.Equal(280 - (2 * NotchGeometry.DefaultShoulder), pill.Width, 6);
        Assert.Equal(52, pill.Height);
    }

    // ------------------------------------------------------------------
    // Lâcher
    // ------------------------------------------------------------------

    [Fact]
    public void ReleasedWithoutMomentum_StaysWhereItWasPut()
    {
        var pill = new ScreenRect(700, 500, 250, 52);
        FloatingTarget target = Detachment.Land(pill, 120, -80, Work, Work.CenterX);

        Assert.Equal(FloatingLanding.Stay, target.Landing);
        Assert.Equal(700, target.X);
        Assert.Equal(500, target.Y);
    }

    [Fact]
    public void ReleasedWithoutMomentumOffScreen_IsPulledBackInside()
    {
        var pill = new ScreenRect(1850, 1020, 250, 52);
        FloatingTarget target = Detachment.Land(pill, 0, 0, Work, Work.CenterX);

        Assert.Equal(FloatingLanding.Stay, target.Landing);
        Assert.True(target.X + 250 <= Work.Right - Detachment.EdgeMargin + Tolerance);
        Assert.True(target.Y + 52 <= Work.Bottom - Detachment.EdgeMargin + Tolerance);
    }

    [Fact]
    public void AFlingGoesToTheMagnetNearestTheProjectedPoint()
    {
        // Lâchée au milieu, lancée vers le bas à droite : c'est le coin bas
        // droit, même si le point de lâcher est plus près d'un autre aimant.
        var pill = new ScreenRect(835, 494, 250, 52);
        FloatingTarget target = Detachment.Land(pill, 2400, 2000, Work, Work.CenterX);

        Assert.Equal(FloatingLanding.Magnet, target.Landing);
        Assert.Equal(Work.Right - Detachment.MagnetMargin - 250, target.X, 6);
        Assert.Equal(Work.Bottom - Detachment.MagnetMargin - 52, target.Y, 6);
    }

    [Fact]
    public void AFlingTowardsTheTopMiddleReattaches()
    {
        var pill = new ScreenRect(835, 400, 250, 52);
        FloatingTarget target = Detachment.Land(pill, 0, -1800, Work, Work.CenterX);

        Assert.Equal(FloatingLanding.Reattach, target.Landing);
        Assert.Equal(Work.CenterX - 125, target.X, 6);
        Assert.Equal(Work.Y, target.Y);
    }

    [Fact]
    public void ReleasedGentlyNearTheTopMiddle_Reattaches()
    {
        var pill = new ScreenRect(820, 30, 250, 52);

        Assert.Equal(FloatingLanding.Reattach, Detachment.Land(pill, 0, 0, Work, Work.CenterX).Landing);
    }

    [Fact]
    public void TheTopCornersBelongToTheMagnetsNotToTheAttachment()
    {
        var pill = new ScreenRect(40, 30, 250, 52);

        Assert.False(Detachment.ReachesReattach(pill, Work, Work.CenterX));
        Assert.Equal(FloatingLanding.Stay, Detachment.Land(pill, 0, 0, Work, Work.CenterX).Landing);

        FloatingTarget thrown = Detachment.Land(new ScreenRect(300, 300, 250, 52), -1500, -1500, Work, Work.CenterX);
        Assert.Equal(FloatingLanding.Magnet, thrown.Landing);
        Assert.Equal(Work.X + Detachment.MagnetMargin, thrown.X, 6);
        Assert.Equal(Work.Y + Detachment.MagnetMargin, thrown.Y, 6);
    }

    [Fact]
    public void EveryMagnetKeepsThePillInsideTheWorkArea()
    {
        var work = new ScreenRect(-1280, 40, 1280, 984);
        var random = new Random(4);

        for (int i = 0; i < 200; i++)
        {
            var projected = new ScreenRect(
                work.X + (random.NextDouble() * 3000) - 1000,
                work.Y + (random.NextDouble() * 3000) - 1000,
                250,
                52);

            (double x, double y) = Detachment.NearestMagnet(projected, work);

            Assert.InRange(x, work.X, work.Right - 250);
            Assert.InRange(y, work.Y, work.Bottom - 52);
        }
    }

    // ------------------------------------------------------------------
    // Ouverture d'une notch flottante
    // ------------------------------------------------------------------

    [Fact]
    public void AFloatingNotchOpensTowardsTheFreeSpace()
    {
        var high = new ScreenRect(800, 120, 250, 52);
        var low = new ScreenRect(800, 900, 250, 52);

        Assert.Equal(FloatingExpansion.Down, Detachment.ExpansionFor(high, Work));
        Assert.Equal(FloatingExpansion.Up, Detachment.ExpansionFor(low, Work));

        var expanded = new IslandFootprint(420, 200);

        ScreenRect down = Detachment.Anchor(high, expanded, Work);
        Assert.Equal(high.Y, down.Y, 6);
        Assert.Equal(high.CenterX, down.CenterX, 6);

        ScreenRect up = Detachment.Anchor(low, expanded, Work);
        Assert.Equal(low.Bottom, up.Bottom, 6);
    }

    [Fact]
    public void AFloatingNotchNeverOpensOffScreen()
    {
        var corner = new ScreenRect(Work.Right - 16 - 250, Work.Bottom - 16 - 52, 250, 52);
        ScreenRect open = Detachment.Anchor(corner, new IslandFootprint(420, 300), Work);

        Assert.True(open.Right <= Work.Right - Detachment.EdgeMargin + Tolerance);
        Assert.True(open.Bottom <= Work.Bottom - Detachment.EdgeMargin + Tolerance);
        Assert.True(open.X >= Work.X && open.Y >= Work.Y);
    }

    [Fact]
    public void TheRestFootprintAnchorsExactlyOnThePill()
    {
        var pill = new ScreenRect(640, 300, 250, 52);

        Assert.Equal(pill, Detachment.Anchor(pill, new IslandFootprint(250, 52), Work));
    }

    // ------------------------------------------------------------------
    // Silhouette flottante
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(250, 52)]
    [InlineData(36, 36)]
    [InlineData(420, 220)]
    public void TheFloatingSilhouetteIsClosedOnAllSidesAndInsideItsBox(double width, double height)
    {
        ShapePoint[] points = NotchGeometry.Default.FloatingSilhouette(new IslandFootprint(width, height));

        Assert.All(points, p =>
        {
            Assert.InRange(p.X, -Tolerance, width + Tolerance);
            Assert.InRange(p.Y, -Tolerance, height + Tolerance);
        });

        // Coins supérieurs arrondis : ni le coin haut gauche ni le coin haut
        // droit ne sont atteints — c'est ce qui la distingue de la notch.
        Assert.DoesNotContain(points, p => Math.Abs(p.X) < Tolerance && Math.Abs(p.Y) < Tolerance);
        Assert.DoesNotContain(points, p => Math.Abs(p.X - width) < Tolerance && Math.Abs(p.Y) < Tolerance);

        // Elle touche ses quatre côtés.
        Assert.Contains(points, p => Math.Abs(p.Y) < Tolerance);
        Assert.Contains(points, p => Math.Abs(p.Y - height) < Tolerance);
        Assert.Contains(points, p => Math.Abs(p.X) < Tolerance);
        Assert.Contains(points, p => Math.Abs(p.X - width) < Tolerance);
    }

    [Fact]
    public void ACompactFloatingNotchIsAPill()
    {
        var compact = new IslandFootprint(250, 44);

        Assert.Equal(22, NotchGeometry.Default.FloatingRadiusFor(compact), 6);
        Assert.Equal(NotchGeometry.DefaultExpandedRadius, NotchGeometry.Default.FloatingRadiusFor(new IslandFootprint(420, 220)), 6);
    }

    [Fact]
    public void TheFloatingSilhouetteRunsClockwise()
    {
        ShapePoint[] points = NotchGeometry.Default.FloatingSilhouette(new IslandFootprint(250, 52));

        // Aire signée positive dans un repère dont l'axe y descend : sens horaire à l'écran.
        double area = 0;

        for (int i = 0; i < points.Length; i++)
        {
            ShapePoint a = points[i];
            ShapePoint b = points[(i + 1) % points.Length];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        Assert.True(area > 0);
        Assert.Equal(IslandShape.FloatingPointCount, points.Length);
    }

    // ------------------------------------------------------------------
    // Goutte
    // ------------------------------------------------------------------

    [Fact]
    public void TheResidueShrinksIntoTheEdgeAndDisappears()
    {
        var attached = new IslandFootprint(280, 52);

        Assert.Equal(attached, GooBridge.Residue(attached, 0));

        double previous = double.MaxValue;

        for (double t = 0; t <= 1.0001; t += 0.05)
        {
            IslandFootprint residue = GooBridge.Residue(attached, t);
            Assert.True(residue.Height <= previous + Tolerance);
            previous = residue.Height;
        }

        Assert.Equal(0, GooBridge.Residue(attached, 1).Height, 6);
    }

    [Fact]
    public void TheNeckThinsThenBreaksThenRetracts()
    {
        var residue = new ScreenRect(820, 0, 280, 52);
        var pill = new ScreenRect(840, 160, 250, 52);

        IReadOnlyList<ShapePoint[]> fused = GooBridge.Neck(residue, pill, 0);
        Assert.Single(fused);

        double widest = Width(fused[0], (residue.Bottom + pill.Y) / 2);
        double thinner = Width(GooBridge.Neck(residue, pill, 0.3)[0], (residue.Bottom + pill.Y) / 2);
        Assert.True(thinner < widest, "Le fil s'amincit.");

        IReadOnlyList<ShapePoint[]> broken = GooBridge.Neck(residue, pill, GooBridge.BreakAt + 0.05);
        Assert.Equal(2, broken.Count);

        Assert.Empty(GooBridge.Neck(residue, pill, 1));
    }

    [Fact]
    public void TheNeckBlendsIntoBothShapes()
    {
        var residue = new ScreenRect(820, 0, 280, 52);
        var pill = new ScreenRect(700, 200, 250, 52);

        ShapePoint[] neck = GooBridge.Neck(residue, pill, 0.1)[0];

        double top = neck.Min(p => p.Y);
        double bottom = neck.Max(p => p.Y);

        Assert.True(top < residue.Bottom, "Le fil entre dans la trace.");
        Assert.True(bottom > pill.Y, "Le fil entre dans la pastille.");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.6)]
    [InlineData(0.75)]
    public void EveryPieceOfTheGooRunsClockwiseLikeTheShapesItJoins(double t)
    {
        // Même sens que la notch et la pastille : sinon la règle de remplissage
        // creuserait un trou là où le fil les recouvre.
        var residue = new ScreenRect(820, 0, 280, 52);
        var pill = new ScreenRect(760, 220, 250, 52);

        Assert.True(GooBridge.SignedArea(NotchGeometry.Default.Silhouette(new IslandFootprint(280, 52))) > 0);

        foreach (ShapePoint[] piece in GooBridge.Neck(residue, pill, t))
        {
            Assert.True(GooBridge.SignedArea(piece) > 0);
        }
    }

    [Fact]
    public void NoNeckWhileTheShapesStillOverlap()
    {
        var residue = new ScreenRect(820, 0, 280, 52);
        var pill = new ScreenRect(835, 40, 250, 52);

        Assert.Empty(GooBridge.Neck(residue, pill, 0));
    }

    private static double Width(ShapePoint[] outline, double y)
    {
        // Largeur du contour à l'ordonnée donnée : les deux points les plus proches de chaque côté.
        IEnumerable<ShapePoint> near = outline.OrderBy(p => Math.Abs(p.Y - y)).Take(2);
        return near.Max(p => p.X) - near.Min(p => p.X);
    }

    // ------------------------------------------------------------------
    // Bulle
    // ------------------------------------------------------------------

    private static IslandActivity Activity(
        string id,
        ActivityRole role = ActivityRole.None,
        ActivityPriority priority = ActivityPriority.Normal,
        int ageSeconds = 0,
        ActivityPresentationPolicy? policy = null)
        => new()
        {
            Id = id,
            FeatureId = "test",
            SceneKey = "test",
            Title = id,
            Role = role,
            Priority = priority,
            Policy = policy,
            CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(1000 - ageSeconds)
        };

    [Fact]
    public void TwoOrdinaryActivitiesNeverSplitTheNotch()
    {
        IslandActivity media = Activity("media");
        IslandActivity weather = Activity("weather", ageSeconds: 30);

        Assert.Null(SplitPresentation.BubbleFor(media, [media, weather]));
    }

    [Fact]
    public void AnImportantActivityBehindTheNotchGetsTheBubble()
    {
        IslandActivity media = Activity("media");
        IslandActivity download = Activity("download", ActivityRole.Download, ageSeconds: 30);

        Assert.Same(download, SplitPresentation.BubbleFor(media, [media, download]));
    }

    [Fact]
    public void SwappingIsReversible()
    {
        IslandActivity media = Activity("media");
        IslandActivity call = Activity("call", ActivityRole.Call);

        // La bulle a été touchée : l'appel prend la notch, la musique devient la bulle.
        Assert.Same(media, SplitPresentation.BubbleFor(call, [media, call]));
        Assert.Same(call, SplitPresentation.BubbleFor(media, [media, call]));
    }

    [Fact]
    public void CriticalPriorityCountsAsImportant()
    {
        IslandActivity media = Activity("media");
        IslandActivity alarm = Activity("alarm", priority: ActivityPriority.Critical);

        Assert.True(SplitPresentation.IsImportant(alarm));
        Assert.Same(alarm, SplitPresentation.BubbleFor(media, [media, alarm]));
    }

    [Fact]
    public void TemporaryOverlaysNeverTakeTheBubble()
    {
        IslandActivity download = Activity("download", ActivityRole.Download);
        IslandActivity volume = Activity("volume", policy: ActivityPresentationPolicy.Temporary);

        Assert.Null(SplitPresentation.BubbleFor(download, [download, volume]));
    }

    [Fact]
    public void ImportantActivitiesWinTheBubbleOverOrdinaryOnes()
    {
        IslandActivity download = Activity("download", ActivityRole.Download);
        IslandActivity media = Activity("media", priority: ActivityPriority.High);
        IslandActivity recording = Activity("mic", ActivityRole.Recording, ageSeconds: 60);

        Assert.Same(recording, SplitPresentation.BubbleFor(download, [download, media, recording]));
    }

    [Fact]
    public void NoPresentedActivityMeansNoBubble()
        => Assert.Null(SplitPresentation.BubbleFor(null, [Activity("download", ActivityRole.Download)]));

    [Fact]
    public void TheAttachedBubbleSitsBesideTheNotchOnTheEdge()
    {
        var screen = new ScreenRect(0, 0, 1920, 1080);
        var notch = new ScreenRect(820, 0, 280, 52);

        ScreenRect bubble = SplitPresentation.AttachedBubbleRect(notch, screen);

        Assert.Equal(notch.Right + SplitPresentation.Gap, bubble.X, 6);
        Assert.Equal(0, bubble.Y);

        var nearEdge = new ScreenRect(1900 - 280, 0, 280, 52);
        Assert.True(SplitPresentation.AttachedBubbleRect(nearEdge, screen).Right <= nearEdge.X);
    }

    [Fact]
    public void TheFloatingBubbleFollowsBesideThePill()
    {
        var pill = new ScreenRect(600, 400, 250, 52);
        ScreenRect bubble = SplitPresentation.FloatingBubbleRect(pill, Work);

        Assert.Equal(pill.Right + SplitPresentation.Gap, bubble.X, 6);
        Assert.Equal(pill.CenterY, bubble.CenterY, 6);

        var right = new ScreenRect(Work.Right - 16 - 250, 400, 250, 52);
        Assert.True(SplitPresentation.FloatingBubbleRect(right, Work).Right <= right.X);
    }
}

public class VelocityTrackerTests
{
    [Fact]
    public void MeasuresASteadyThrow()
    {
        var tracker = new VelocityTracker();

        for (int i = 0; i <= 20; i++)
        {
            double t = i / 120.0;
            tracker.Add(t, 1500 * t, -600 * t);
        }

        (double vx, double vy) = tracker.Velocity(20 / 120.0);

        Assert.Equal(1500, vx, 3);
        Assert.Equal(-600, vy, 3);
    }

    [Fact]
    public void AHandThatStoppedBeforeReleasingHasNoMomentum()
    {
        var tracker = new VelocityTracker();

        for (int i = 0; i <= 10; i++)
        {
            tracker.Add(i / 120.0, 3000 * i / 120.0, 0);
        }

        Assert.Equal((0.0, 0.0), tracker.Velocity((10 / 120.0) + 0.1));
    }

    [Fact]
    public void OnlyRecentSamplesCount()
    {
        var tracker = new VelocityTracker();

        // Lent pendant une seconde, puis un coup sec.
        for (int i = 0; i <= 100; i++)
        {
            tracker.Add(i / 100.0, i, 0);
        }

        for (int i = 1; i <= 8; i++)
        {
            tracker.Add(1 + (i / 100.0), 100 + (i * 20), 0);
        }

        (double vx, _) = tracker.Velocity(1.08);
        Assert.True(vx > 1500);
    }

    [Fact]
    public void TooFewSamplesMeansNoVelocity()
    {
        var tracker = new VelocityTracker();
        Assert.Equal((0.0, 0.0), tracker.Velocity(0));

        tracker.Add(0, 10, 10);
        Assert.Equal((0.0, 0.0), tracker.Velocity(0));
    }
}
