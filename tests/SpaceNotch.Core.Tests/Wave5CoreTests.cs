using System;
using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Vague 5a : la logique pure des gestes et animations.</summary>
public sealed class Wave5CoreTests
{
    // M1 — horloge à palettes
    [Fact]
    public void PixelFont_DrawsEveryDigit_Distinctly()
    {
        var shapes = "0123456789".Select(c => string.Concat(PixelFont.Resolve(c).Select(b => b ? 'x' : '.'))).ToList();

        Assert.All(shapes, s => Assert.Equal(15, s.Length));
        Assert.Equal(10, shapes.Distinct().Count());
    }

    [Fact]
    public void PixelFont_OnlyTheChangedPalettesFlip()
        => Assert.Equal([4], PixelFont.Changed("12:34", "12:35"));

    // M2 — spinner → coche
    [Fact]
    public void SpinnerCheck_EndsExactlyOnTheCheck()
    {
        var end = SpinnerCheck.Morph(0.7, 1);

        Assert.Equal(SpinnerCheck.Done(), end);
        Assert.Equal(SpinnerCheck.Count, SpinnerCheck.Ring(0.3).Count);
    }

    [Fact]
    public void SpinnerCheck_StartsFromTheFrozenRing()
    {
        var ring = SpinnerCheck.Ring(0.7);
        var start = SpinnerCheck.Morph(0.7, 0);

        Assert.All(start, p => Assert.Contains(ring, r => Math.Abs(r.X - p.X) < 1e-9 && Math.Abs(r.Y - p.Y) < 1e-9));
    }

    // A1 — titres qui se décodent
    [Fact]
    public void TextScramble_SettlesLeftToRight_AndKeepsSpaces()
    {
        string half = TextScramble.Frame("Good Days", 0.5, 3);

        Assert.StartsWith("Good", half);
        Assert.Equal(' ', half[4]);
        Assert.NotEqual("Good Days", half);
        Assert.Equal("Good Days", TextScramble.Frame("Good Days", 1, 3));
    }

    [Theory]
    [InlineData("A", "B", true)]
    [InlineData("A", "A", false)]
    [InlineData(null, "A", false)]
    [InlineData("", "A", false)]
    [InlineData("07:42", "07:41", false)]
    [InlineData("62 %", "63 %", false)]
    [InlineData("Downloading", "Downloaded", true)]
    public void TextScramble_PlaysOnlyOnRealChanges(string? before, string after, bool expected)
        => Assert.Equal(expected, TextScramble.ShouldPlay(before, after));

    // F5 — couleur copiée
    [Theory]
    [InlineData("#7FE6FF", 0x7F, 0xE6, 0xFF)]
    [InlineData("  #7fe ", 0x77, 0xFF, 0xEE)]
    [InlineData("rgb(127, 230, 255)", 127, 230, 255)]
    [InlineData("rgba(10 20 30 / 50%)", 10, 20, 30)]
    [InlineData("hsl(0, 100%, 50%)", 255, 0, 0)]
    public void ColorCode_ReadsTheCommonFormats(string text, int r, int g, int b)
    {
        Assert.True(ColorCode.TryParse(text, out ColorCode color));
        Assert.Equal(((byte)r, (byte)g, (byte)b), (color.R, color.G, color.B));
    }

    [Theory]
    [InlineData("Code wifi : 4F7K")]
    [InlineData("#12345")]
    [InlineData("rgb(300, 0, 0)")]
    [InlineData("https://example.com/#FFFFFF")]
    [InlineData(null)]
    public void ColorCode_IgnoresAnythingElse(string? text)
        => Assert.False(ColorCode.TryParse(text, out _));

    [Fact]
    public void ColorCode_WritesTheThreeFormats()
    {
        Assert.True(ColorCode.TryParse("#FF0000", out ColorCode red));

        Assert.Equal("#FF0000", red.Hex);
        Assert.Equal("rgb(255, 0, 0)", red.Rgb);
        Assert.Equal("hsl(0, 100%, 50%)", red.Hsl);
        Assert.False(red.IsLight);
    }

    // D2 — couleur tirée de la pochette
    [Fact]
    public void DominantColor_FollowsTheColouredArea_NotTheGrey()
    {
        // Moitié gris neutre, moitié rouge vif.
        byte[] pixels = new byte[64 * 4];

        for (int i = 0; i < 64; i++)
        {
            (byte b, byte g, byte r) = i < 32 ? ((byte)128, (byte)128, (byte)128) : ((byte)20, (byte)20, (byte)220);
            pixels[(i * 4) + 0] = b;
            pixels[(i * 4) + 1] = g;
            pixels[(i * 4) + 2] = r;
            pixels[(i * 4) + 3] = 255;
        }

        ActivityTint tint = DominantColor.FromBgra(pixels)!.Value;

        Assert.True(tint.R > tint.G + 60 && tint.R > tint.B + 60);
    }

    [Fact]
    public void DominantColor_OfAGreyImage_IsNothing()
    {
        byte[] grey = Enumerable.Repeat((byte)128, 64 * 4).ToArray();

        Assert.Null(DominantColor.FromBgra(grey));
    }

    [Fact]
    public void DominantColor_IsReadableOnBlack()
    {
        ActivityTint dull = DominantColor.ForBlack(new ColorCode(40, 30, 60));
        var (_, s, l) = new ColorCode(dull.R, dull.G, dull.B).ToHsl();

        Assert.InRange(l, 0.55, 0.75);
        Assert.True(s >= 0.5);
    }

    // S4 — fader cranté
    [Fact]
    public void VolumeFader_LightsOneTickPerTenPercent()
    {
        Assert.Equal(1, VolumeFader.LitTicks(0));
        Assert.Equal(8, VolumeFader.LitTicks(0.72));
        Assert.Equal(11, VolumeFader.LitTicks(1));
        Assert.Equal(2, VolumeFader.Crossed(0.45, 0.62));
        Assert.Equal(0, VolumeFader.Crossed(0.41, 0.49));
    }

    [Fact]
    public void VolumeFader_WheelStepsByTwoPercent_WithinBounds()
    {
        Assert.Equal(0.74, VolumeFader.Wheel(0.72, 1), 6);
        Assert.Equal(0, VolumeFader.Wheel(0.01, -1), 6);
        Assert.Equal(1, VolumeFader.Wheel(0.99, 3), 6);
    }

    // S1 — éventail
    [Fact]
    public void CardFan_OpensWithSpreadAndAlternatingTilt()
    {
        var open = CardFan.Layout(5, open: true);

        Assert.Equal(CardFan.MaxCards, open.Count);
        Assert.Equal(CardFan.Spread * 2, open[2].Offset);
        Assert.Equal(1.5, open[1].Rotation);
        Assert.Equal(-1.5, open[2].Rotation);
        Assert.True(CardFan.Width(3, true, 130) > CardFan.Width(3, false, 130));
    }

    // U4 — notch magnétique
    [Fact]
    public void Magnet_PullsTowardTheCursor_OnlyWithinReach()
    {
        var notch = new ScreenRect(800, 0, 200, 34);

        MagnetPull near = Magnet.For(900, 80, notch);
        Assert.True(near.DY > 0 && Math.Abs(near.DX) < 0.5 && near.Scale > 1);
        Assert.True(near.DY <= Magnet.MaxShift);

        Assert.Equal(MagnetPull.None, Magnet.For(900, 400, notch));
        Assert.Equal(MagnetPull.None, Magnet.For(900, 10, notch)); // dessus : le survol prend le relais
    }

    // M4 — rayons
    [Fact]
    public void LightRays_AppearThenLeaveEverythingBlack()
    {
        Assert.Empty(LightRays.At(0, 300));
        Assert.NotEmpty(LightRays.At(0.3, 300));
        Assert.Empty(LightRays.At(LightRays.Seconds, 300));
        Assert.All(LightRays.At(0.3, 300), p => Assert.Equal(0, p.X % LightRays.PixelDip, 6));
    }

    [Fact]
    public void LightRays_CelebrateOnlyTheMomentOfSuccess()
    {
        var working = new IslandActivity { Id = "d", FeatureId = "t", SceneKey = "card", Title = "x", MotionState = ActivityMotionState.Working };
        var done = new IslandActivity { Id = "d", FeatureId = "t", SceneKey = "card", Title = "x", MotionState = ActivityMotionState.Completing };

        Assert.True(LightRays.Celebrates(working, done));
        Assert.True(LightRays.Celebrates(null, done));
        Assert.False(LightRays.Celebrates(done, done));
        Assert.False(LightRays.Celebrates(done, working));
    }

    [Fact]
    public void CompactTrailing_SpinsWhileDownloading_ThenShowsACheck()
    {
        var downloading = new IslandActivity { Id = "d", FeatureId = "t", SceneKey = "card", Title = "x", Role = ActivityRole.Download, MotionState = ActivityMotionState.Working };
        var downloaded = new IslandActivity { Id = "d", FeatureId = "t", SceneKey = "card", Title = "x", IconKey = "Check", Metric = "✓", MotionState = ActivityMotionState.Completing };

        Assert.Equal(TrailingKind.Spinner, CompactTrailing.For(downloading).Kind);
        Assert.Equal(TrailingKind.Check, CompactTrailing.For(downloaded).Kind);
        Assert.Null(CompactTrailing.MetricFor(downloaded, CompactTrailing.For(downloaded)));
    }

    // F11 — pomodoro autour de la silhouette
    [Fact]
    public void TheFocusLevel_IsOpenAtTheTop_AndShrinksTowardTheBottom()
    {
        ShapePoint[] ring = OutlineTrim.Ring(300, 60, 24, IslandShape.Squircle, 12);
        ShapePoint[] level = OutlineTrim.OpenTop(ring);
        double top = OutlineTrim.RingInset;

        Assert.NotEmpty(level);

        // Seuls les deux bouts touchent le haut : aucun segment ne longe le bord de l'écran.
        Assert.Equal(2, level.Count(p => p.Y <= top + 0.01));
        Assert.True(level[0].Y <= top + 0.01 && level[^1].Y <= top + 0.01);

        double full = OutlineTrim.Length(OutlineTrim.Centered(level, 1)) ;
        var half = OutlineTrim.Centered(level, 0.5);
        Assert.Empty(OutlineTrim.Centered(level, 0));

        // Symétrique autour du milieu du bas.
        Assert.Equal(300 - half[0].X, half[^1].X, 1);
        Assert.Equal(half[0].Y, half[^1].Y, 1);
        Assert.True(half.All(p => p.Y > top + 0.01));
        Assert.True(full > 0);
    }

    [Fact]
    public void TheFocusRing_StaysInsideTheNotch_AndStartsAtTheBottom()
    {
        ShapePoint[] ring = OutlineTrim.Ring(300, 60, 24, IslandShape.Squircle, 12);

        Assert.NotEmpty(ring);
        Assert.All(ring, p =>
        {
            Assert.InRange(p.X, 12 + OutlineTrim.RingInset - 0.01, 300 - 12 - OutlineTrim.RingInset + 0.01);
            Assert.InRange(p.Y, OutlineTrim.RingInset - 0.01, 60 - OutlineTrim.RingInset + 0.01);
        });

        var trace = OutlineTrim.Trim(ring, 0.25);
        Assert.Equal(60 - OutlineTrim.RingInset, trace[0].Y, 3);
        Assert.Equal(150, trace[0].X, 0);

        // Dans le sens horaire : depuis le bas, le tracé part vers la gauche.
        Assert.True(trace[1].X < trace[0].X);
        Assert.Empty(OutlineTrim.Ring(30, 12, 6, IslandShape.Squircle, 4));
    }

    [Fact]
    public void OutlineTrim_ShrinksWithTheRemainingTime()
    {
        ShapePoint[] square = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)];

        double full = OutlineTrim.Length(OutlineTrim.Trim(square, 1)) ;
        var half = OutlineTrim.Trim(square, 0.5);

        Assert.Equal(400, OutlineTrim.Length(square), 6);
        Assert.Empty(OutlineTrim.Trim(square, 0));
        Assert.True(half.Count >= 2);
        Assert.Equal(200, PathLength(half), 6);
        Assert.True(full > 0);
    }

    // F9 — ne pas déranger
    [Fact]
    public void QuietSummary_GroupsByApp_MostTalkativeFirst()
    {
        var quiet = new QuietSummary();
        DateTimeOffset t = DateTimeOffset.UnixEpoch;
        quiet.Hold("Teams", "a", t);
        quiet.Hold("Discord", "b", t);
        quiet.Hold("discord", "c", t.AddMinutes(1));

        var groups = quiet.Groups();

        Assert.Equal(3, quiet.Count);
        Assert.Equal("Discord", groups[0].App);
        Assert.Equal(2, groups[0].Count);
        Assert.Equal("c", groups[0].Latest);
    }

    // S3 — éclatement
    [Fact]
    public void PixelShatter_FallsAndFades()
    {
        var early = PixelShatter.At(4, 8, 14, 0.05);
        var late = PixelShatter.At(4, 8, 14, 0.4);

        Assert.NotEmpty(early);
        Assert.True(late.Average(s => s.Y) > early.Average(s => s.Y));
        Assert.True(late.Max(s => s.Opacity) < early.Max(s => s.Opacity));
        Assert.Empty(PixelShatter.At(4, 8, 14, PixelShatter.Seconds));
    }

    // A2 — fondu en pixels
    [Fact]
    public void PixelDissolve_CoversEverythingAtTheSwap_ThenUncovers()
    {
        for (int c = 0; c < 8; c++)
        {
            for (int r = 0; r < 4; r++)
            {
                Assert.Equal(1, PixelDissolve.Cover(c, r, 8, 4, PixelDissolve.SwapAt), 6);
                Assert.Equal(0, PixelDissolve.Cover(c, r, 8, 4, 1), 6);
                Assert.Equal(0, PixelDissolve.Cover(c, r, 8, 4, 0), 6);
            }
        }
    }

    private static double PathLength(System.Collections.Generic.IReadOnlyList<ShapePoint> p)
    {
        double length = 0;

        for (int i = 0; i + 1 < p.Count; i++)
        {
            length += Math.Sqrt(Math.Pow(p[i + 1].X - p[i].X, 2) + Math.Pow(p[i + 1].Y - p[i].Y, 2));
        }

        return length;
    }
}
