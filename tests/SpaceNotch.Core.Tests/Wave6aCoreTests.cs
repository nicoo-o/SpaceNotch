using System;
using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Animation;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Vague 6a : le caractère de la notch — regard, matière, poids, et ses micro-gestes.</summary>
public class Wave6aCoreTests
{
    // ---------------- P1 Pixel ----------------

    [Fact]
    public void Gaze_FollowsThePointer_WithoutLeavingTheEye()
    {
        Assert.Equal((0.0, 0.0), PixelGaze.Look(0, 0));

        (double lx, _) = PixelGaze.Look(-80, 0);
        (double rx, _) = PixelGaze.Look(80, 0);
        Assert.True(lx < 0 && rx > 0);
        Assert.Equal(-lx, rx, 6);

        (double fx, double fy) = PixelGaze.Look(1e6, 1e6);
        Assert.InRange(fx, 0, PixelGaze.MaxLookX);
        Assert.InRange(fy, 0, PixelGaze.MaxLookY);
        Assert.Equal((0.0, 0.0), PixelGaze.Look(double.NaN, 3));
    }

    [Fact]
    public void Mood_NotificationWinsOverHoverAndSleep()
    {
        var night = new TimeOnly(2, 14);
        var day = new TimeOnly(14, 32);

        Assert.Equal(PixelMood.Surprised, PixelGaze.MoodFor(notification: true, hovered: true, idle: true, night));
        Assert.Equal(PixelMood.Pleased, PixelGaze.MoodFor(false, hovered: true, idle: false, night));
        Assert.Equal(PixelMood.Asleep, PixelGaze.MoodFor(false, false, false, night));
        Assert.Equal(PixelMood.Asleep, PixelGaze.MoodFor(false, false, idle: true, day));
        Assert.Equal(PixelMood.Awake, PixelGaze.MoodFor(false, false, false, day));
    }

    [Theory]
    [InlineData(22, 59, false)]
    [InlineData(23, 0, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]
    public void Night_RunsFromElevenToSeven(int hour, int minute, bool night)
        => Assert.Equal(night, PixelGaze.IsNight(new TimeOnly(hour, minute)));

    [Fact]
    public void Shapes_ReadAsMoods_AndBlinksAreIrregularButBounded()
    {
        Assert.True(PixelGaze.Shape(PixelMood.Pleased).Height < PixelGaze.Shape(PixelMood.Awake).Height);
        Assert.Equal(1, PixelGaze.Shape(PixelMood.Surprised).Roundness);
        Assert.True(PixelGaze.Shape(PixelMood.Asleep).Height <= 2);

        TimeSpan[] blinks = Enumerable.Range(0, 40).Select(PixelGaze.NextBlink).ToArray();
        Assert.All(blinks, b => Assert.InRange(b.TotalSeconds, 2.5, 6.5));
        Assert.True(blinks.Distinct().Count() > 20);
        Assert.Equal(PixelGaze.NextBlink(7), PixelGaze.NextBlink(7));
    }

    // ---------------- P3 Lancer ----------------

    [Fact]
    public void ThrownNotch_BouncesOffTheEdge_SquashesAndComesToRest()
    {
        var bounds = new ScreenRect(100, 20, 800, 400);
        var body = new NotchBody(500, 100, 3000, -800, bounds);
        bool squashed = false;

        for (int i = 0; i < 200 && !body.IsSpent; i++)
        {
            body.Step(1 / 60.0);
            Assert.InRange(body.X, bounds.X, bounds.Right);
            Assert.InRange(body.Y, bounds.Y, bounds.Bottom);
            squashed |= body.Squash.X < 0.97 || body.Squash.Y < 0.97;
        }

        Assert.True(body.Bounces >= 1);
        Assert.True(squashed);
        Assert.True(body.IsSpent);
    }

    [Fact]
    public void GentleThrow_NeverBounces_AndSquashRelaxes()
    {
        var body = new NotchBody(500, 200, 300, 0, new ScreenRect(0, 0, 1000, 400));

        for (int i = 0; i < 120; i++)
        {
            body.Step(1 / 60.0);
        }

        Assert.Equal(0, body.Bounces);
        Assert.Equal(1, body.Squash.X, 3);
        Assert.Equal(1, body.Squash.Y, 3);
    }

    [Fact]
    public void Flight_EndsAfterItsMaximumTime()
    {
        var body = new NotchBody(50, 50, 5000, 5000, new ScreenRect(0, 0, 100, 100));
        body.Step(NotchBody.MaxFlightSeconds + 0.01);
        Assert.True(body.IsSpent);
    }

    // ---------------- P4 Sablier ----------------

    [Fact]
    public void Heap_OnePixelPerPercent_StaysLevel_AndNeverUndoes()
    {
        var heap = new PixelHeap(40);
        Assert.Equal(55, heap.FillTo(55).Count);
        Assert.Empty(heap.FillTo(30));
        Assert.Equal(45, heap.FillTo(100).Count);
        Assert.Equal(100, heap.Count);

        // 100 pixels sur 40 colonnes : 2 ou 3 de haut, jamais de pic.
        Assert.Equal(3, heap.Height);
        Assert.Equal(heap.Placed.Count, heap.Placed.Distinct().Count());
    }

    [Fact]
    public void Heap_IsTheSameEveryTime()
    {
        var a = new PixelHeap(30);
        var b = new PixelHeap(30);
        a.FillTo(70);
        b.FillTo(70);
        Assert.Equal(a.Placed, b.Placed);
        Assert.Equal(66, PixelHeap.ColumnsFor(266));
    }

    // ---------------- P6 Glyphes qui migrent ----------------

    [Theory]
    [InlineData("Play", "Pause")]
    [InlineData("Music", "Bluetooth")]
    [InlineData("Check", "Music")]
    public void Morph_EveryNewPixelComesFromAnOldOne(string fromKey, string toKey)
    {
        var from = PixelGlyphs.Resolve(fromKey);
        var to = PixelGlyphs.Resolve(toKey);
        Assert.NotNull(from);
        Assert.NotNull(to);

        var moves = GlyphMorph.Pair(from!, to!);

        Assert.Equal(to!.Count(b => b), moves.Count);
        Assert.All(moves, m => Assert.True(to[m.To] && from![m.From]));
        Assert.Equal(moves.Count, moves.Select(m => m.To).Distinct().Count());
        Assert.All(moves, m => Assert.InRange(m.Order, 0, 1));
    }

    [Fact]
    public void Morph_OfAnEmptyGlyph_IsNothing()
    {
        var empty = new bool[49];
        Assert.Empty(GlyphMorph.Pair(empty, PixelGlyphs.Resolve("Music")!));
        var move = new PixelMove(To: 8, From: 0, Order: 0);
        Assert.Equal((-1, -1), GlyphMorph.Offset(move));
    }

    // ---------------- P2 Goutte, A8 coins crénelés ----------------

    [Fact]
    public void Drop_HangsTowardThePointer_InsideTheWindow()
    {
        const double width = 300, height = 60;
        double body = height - ShapeEffects.DropDepth;
        ShapePoint[] outline = IslandShape.Silhouette(width, body, 18, IslandShape.Squircle, 0, 8);
        ShapePoint[] liquid = ShapeEffects.Liquid(outline, body, dropX: 200, drop: 1, ripple: 0, phase: 0);

        Assert.True(liquid.Length > outline.Length + 20);
        Assert.All(liquid, p => Assert.InRange(p.Y, 0, height + 1e-9));

        ShapePoint deepest = liquid.MaxBy(p => p.Y);
        Assert.InRange(deepest.X, 196, 204);
        Assert.Equal(height, deepest.Y, 1);

        Assert.Same(outline, ShapeEffects.Liquid(outline, body, 200, 0, 0, 0));
    }

    [Fact]
    public void Ripple_StaysWithinItsAmplitude_AndFadesAway()
    {
        for (double x = 0; x < 400; x += 3)
        {
            Assert.InRange(ShapeEffects.Profile(x, 150, 0, 6, 1.3), -6, ShapeEffects.DropDepth);
        }

        double near = Math.Abs(ShapeEffects.Profile(160, 150, 0, 6, 0));
        double far = Math.Abs(ShapeEffects.Profile(150 + 400 + 11.5, 150, 0, 6, 0));
        Assert.True(far < 6 * 0.1);
        Assert.True(near <= 6);
    }

    [Fact]
    public void Crenellation_TurnsTheBottomCornersIntoStairs()
    {
        const double width = 340, height = 56, radius = 24;
        ShapePoint[] smooth = IslandShape.Silhouette(width, height, radius, IslandShape.Squircle, 0, 8);
        ShapePoint[] stairs = ShapeEffects.Crenellate(smooth, height, radius);

        Assert.All(stairs, p => Assert.InRange(p.Y, 0, height));

        // Dans les coins, chaque segment est horizontal ou vertical.
        for (int i = 0; i < stairs.Length - 1; i++)
        {
            ShapePoint a = stairs[i], b = stairs[i + 1];

            if (a.Y > height - radius && b.Y > height - radius)
            {
                Assert.True(a.X == b.X || a.Y == b.Y, $"Segment oblique {a} → {b}");
            }
        }

        // Le haut (bord de l'écran, épaules) n'a pas bougé.
        Assert.Equal(smooth.Where(p => p.Y < 10), stairs.Where(p => p.Y < 10));
        Assert.Same(smooth, ShapeEffects.Crenellate(smooth, height, radius: 2));
    }

    // ---------------- A1, A3 ----------------

    [Fact]
    public void Afterglow_KeepsOnlyThePixelsThatTurnOff()
    {
        var three = PixelFont.Resolve('3');
        var four = PixelFont.Resolve('4');
        var fading = Afterglow.Fading(three, four);

        Assert.NotEmpty(fading);
        Assert.All(fading, i => Assert.True(three[i] && !four[i]));
        Assert.Empty(Afterglow.Fading(four, four));
    }

    [Theory]
    [InlineData("21°", "22°", 1, 1)]
    [InlineData("14:32", "14:33", 4, 1)]
    [InlineData("9 %", "10 %", 0, 2)]
    [InlineData("100 %", "99 %", 0, 2)]
    [InlineData("22°", "22°", 0, 0)]
    [InlineData(null, "22°", 0, 0)]
    public void Ink_InvertsOnlyWhatChanged(string? before, string after, int start, int length)
        => Assert.Equal((start, length), Afterglow.InkSpan(before, after));

    // ---------------- A5, A7, A9 ----------------

    [Fact]
    public void InkRing_IsACrispCircle_ThatGrowsAndFades()
    {
        foreach (int r in new[] { 1, 4, 9, 13 })
        {
            var circle = InkRing.Circle(r);
            Assert.All(circle, p => Assert.InRange(Math.Sqrt((p.X * p.X) + (p.Y * p.Y)), r - 1, r + 1));
            Assert.Equal(circle.Count, circle.Distinct().Count());
        }

        int[] radii = Enumerable.Range(0, InkRing.Frames).Select(InkRing.RadiusAt).ToArray();
        Assert.Equal(radii.OrderBy(r => r), radii);
        Assert.Equal(InkRing.MaxRadius, radii[^1]);
        Assert.True(InkRing.OpacityAt(0) > InkRing.OpacityAt(InkRing.Frames - 1));
    }

    [Fact]
    public void Spring_ReadsTheImportance_ButRespectsCalm()
    {
        SpringParameters natural = MotionPresets.NaturalOpen;

        Assert.Equal(1.0, MotionPresets.ForPriority(natural, ActivityPriority.Normal).DampingRatio, 2);
        Assert.Equal(MotionPresets.UrgentDamping, MotionPresets.ForPriority(natural, ActivityPriority.Critical).DampingRatio, 2);
        Assert.Equal(natural.ResponseSeconds, MotionPresets.ForPriority(natural, ActivityPriority.High).ResponseSeconds, 2);

        SpringParameters calm = MotionPresets.Spring(MotionStyle.Quiet);
        Assert.Same(calm, MotionPresets.ForPriority(calm, ActivityPriority.Critical));

        Assert.Equal(0, MotionPresets.Bump[^1]);
        Assert.True(Math.Abs(MotionPresets.Bump[0]) <= 2.5);
    }

    [Fact]
    public void Identicon_IsStableSymmetricAndNeverBlank()
    {
        bool[] a = Identicon.From("build.ps1");
        Assert.Equal(a, Identicon.From("  BUILD.PS1 "));
        Assert.NotEqual(a, Identicon.From("backup.bat"));

        foreach (string name in new[] { "", "x", "MonOutil.exe", "build.ps1", "backup.bat" })
        {
            bool[] m = Identicon.From(name);
            Assert.Equal(25, m.Length);
            Assert.InRange(m.Count(b => b), 1, 24);

            for (int row = 0; row < 5; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    Assert.Equal(m[(row * 5) + col], m[(row * 5) + (4 - col)]);
                }
            }
        }

        Assert.Equal(0, Identicon.GrowDelay(12));
        Assert.True(Identicon.GrowDelay(0) > Identicon.GrowDelay(6));
        Assert.InRange(Identicon.PaletteIndex("build.ps1", 5), 0, 4);
    }

    // ---------------- A4 Projecteur tramé ----------------

    [Fact]
    public void Spotlight_LightsTheTrameAroundThePointerOnly()
    {
        Assert.Equal(0, TrameField.Spot(10, 10, null, 70));
        Assert.Equal(0, TrameField.Spot(200, 10, (10, 10), 70));
        Assert.True(TrameField.Spot(12, 10, (10, 10), 70) > 0.8);

        var none = TrameField.Cells(360, 120, 3, 24, 8, []);
        var lit = TrameField.Cells(360, 120, 3, 24, 8, [], spot: (180, 100), spotRadius: 70);

        Assert.True(lit.Count > none.Count);
        Assert.All(none, c => Assert.Contains(c, lit));

        // Au-dessus du contenu, le projecteur allume la trame, mais jamais contre un texte.
        var text = new TrameRect(40, 30, 320, 90);
        var high = TrameField.Cells(360, 120, 3, 24, 8, [text], spot: (180, 20), spotRadius: 70);
        Assert.NotEmpty(high);
        Assert.All(high, c => Assert.False(text.Inflate(0).Contains((c.Column + 0.5) * 3, (c.Row + 0.5) * 3)));
    }
}
